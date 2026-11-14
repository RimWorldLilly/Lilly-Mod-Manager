using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Lilly.IntegratedModManager
{
    internal static class WorkshopPreviewGallery
    {
        internal enum WorkshopItemStatus
        {
            Unknown,
            Checking,
            Available,
            Missing,
            CheckFailed
        }

        internal sealed class GalleryImage
        {
            internal byte[] Bytes;
            internal bool StillFailed;
            internal Texture2D Still;
            internal List<GifDecoder.GifFrame> GifFrames;
            internal Texture2D[] GifTextures;
            internal float[] GifFrameEnds;
            internal float GifDuration;
        }

        internal sealed class GalleryEntry
        {
            internal volatile bool Loading;
            internal volatile GalleryImage[] Images;
            internal volatile string Error;
            internal int NextStillIndex;
            internal int LastStillDecodeFrame = -1;
        }

        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private static readonly ConcurrentDictionary<ulong, GalleryEntry> Entries =
            new ConcurrentDictionary<ulong, GalleryEntry>();
        private static readonly ConcurrentDictionary<ulong, WorkshopItemStatus> ItemStatuses =
            new ConcurrentDictionary<ulong, WorkshopItemStatus>();
        private static readonly SemaphoreSlim AvailabilityRequestLimit = new SemaphoreSlim(2, 2);
        private static readonly Regex WorkshopResultRegex = new Regex(
            @"\""result\""\s*:\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UrlRegex = new Regex(
            @"https?:\\?/\\?/[^\""'\s<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private const int MaxPreviewImages = 6;
        private const int MaxCachedEntries = 24;
        private const int MaxPreviewImageBytes = 8 * 1024 * 1024;
        private const int MaxStillTextureSize = 1024;
        private const int MaxGifFrames = 32;
        private const int MaxGifWidth = 480;
        private const int MaxGifHeight = 480;
        private const long MaxGifPixels = 4500000L;

        internal static GalleryEntry Request(ulong workshopId)
        {
            if (workshopId == 0UL)
                return null;
            TrimCache(workshopId);
            GalleryEntry entry = Entries.GetOrAdd(workshopId, _ => new GalleryEntry());
            if (!entry.Loading && entry.Images == null && entry.Error == null)
            {
                entry.Loading = true;
                Task.Run(() => LoadAsync(workshopId, entry));
            }
            return entry;
        }

        internal static WorkshopItemStatus RequestAvailability(ulong workshopId)
        {
            if (workshopId == 0UL)
                return WorkshopItemStatus.Unknown;
            WorkshopItemStatus status;
            if (!ItemStatuses.TryGetValue(workshopId, out status))
            {
                if (ItemStatuses.TryAdd(workshopId, WorkshopItemStatus.Checking))
                    Task.Run(() => CheckAvailabilityAsync(workshopId));
                return WorkshopItemStatus.Checking;
            }
            return status;
        }

        private static async Task CheckAvailabilityAsync(ulong workshopId)
        {
            await AvailabilityRequestLimit.WaitAsync().ConfigureAwait(false);
            try
            {
                using (HttpContent content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("itemcount", "1"),
                    new KeyValuePair<string, string>("publishedfileids[0]", workshopId.ToString())
                }))
                using (HttpResponseMessage response = await Client.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    content).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Match resultMatch = WorkshopResultRegex.Match(json);
                    if (!resultMatch.Success)
                    {
                        ItemStatuses[workshopId] = WorkshopItemStatus.CheckFailed;
                        return;
                    }

                    int result;
                    if (!int.TryParse(resultMatch.Groups[1].Value, out result))
                        ItemStatuses[workshopId] = WorkshopItemStatus.CheckFailed;
                    else
                        ItemStatuses[workshopId] = result == 1
                            ? WorkshopItemStatus.Available
                            : result == 9
                                ? WorkshopItemStatus.Missing
                                : WorkshopItemStatus.CheckFailed;
                }
            }
            catch (Exception)
            {
                ItemStatuses[workshopId] = WorkshopItemStatus.CheckFailed;
            }
            finally
            {
                AvailabilityRequestLimit.Release();
            }
        }

        internal static GalleryImage[] GetImages(GalleryEntry entry)
        {
            if (entry == null || entry.Images == null)
                return Array.Empty<GalleryImage>();
            GalleryImage[] images = entry.Images;
            if (entry.NextStillIndex < images.Length &&
                entry.LastStillDecodeFrame != Time.frameCount)
            {
                entry.LastStillDecodeFrame = Time.frameCount;
                EnsureStillTexture(images[entry.NextStillIndex]);
                entry.NextStillIndex++;
            }
            return images;
        }

        internal static Texture2D GetStillTexture(GalleryImage image)
        {
            EnsureStillTexture(image);
            return image == null ? null : image.Still;
        }

        internal static Texture2D GetTexture(GalleryImage image, float time)
        {
            if (image == null)
                return null;
            if (IsAnimated(image))
            {
                EnsureStillTexture(image);
                return GetAnimationTexture(image, time);
            }
            EnsureStillTexture(image);
            return image.Still;
        }

        private static bool IsAnimated(GalleryImage image)
        {
            if (image == null)
                return false;
            if (image.GifTextures != null)
                return image.GifTextures.Length > 1;
            return image.GifFrames != null && image.GifFrames.Count > 1;
        }

        private static void EnsureStillTexture(GalleryImage image)
        {
            if (image == null || image.Still != null || image.StillFailed)
                return;
            if (image.GifFrames != null && image.GifFrames.Count > 0)
            {
                image.Still = CreateTexture(image.GifFrames[0]);
                return;
            }
            if (image.GifTextures != null && image.GifTextures.Length > 0)
            {
                image.Still = image.GifTextures[0];
                return;
            }
            byte[] bytes = image.Bytes;
            if (bytes == null || bytes.Length == 0)
                return;
            try
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, bytes))
                {
                    texture.filterMode = FilterMode.Bilinear;
                    image.Still = LimitTextureSize(texture);
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                    image.StillFailed = true;
                }
            }
            catch (Exception)
            {
                image.StillFailed = true;
            }
            finally
            {
                image.Bytes = null;
            }
        }

        private static Texture2D GetAnimationTexture(GalleryImage image, float time)
        {
            if (image.GifTextures == null)
                InitializeAnimation(image);
            if (image.GifTextures == null || image.GifTextures.Length == 0)
            {
                EnsureStillTexture(image);
                return image.Still;
            }

            float position = image.GifDuration > 0f
                ? Mathf.Repeat(Mathf.Max(0f, time), image.GifDuration)
                : 0f;
            int frameIndex = image.GifFrameEnds.Length - 1;
            for (int i = 0; i < image.GifFrameEnds.Length; i++)
            {
                if (position < image.GifFrameEnds[i])
                {
                    frameIndex = i;
                    break;
                }
            }

            Texture2D texture = image.GifTextures[frameIndex];
            if (texture == null)
            {
                if (image.GifFrames != null && frameIndex < image.GifFrames.Count)
                    texture = CreateTexture(image.GifFrames[frameIndex]);
                if (texture == null)
                    texture = image.Still;
                image.GifTextures[frameIndex] = texture;
            }
            ReleaseFramePixelsIfComplete(image);
            return texture;
        }

        private static void InitializeAnimation(GalleryImage image)
        {
            if (image == null || image.GifFrames == null || image.GifFrames.Count < 2)
                return;
            image.GifTextures = new Texture2D[image.GifFrames.Count];
            image.GifFrameEnds = new float[image.GifFrames.Count];
            if (image.Still != null)
                image.GifTextures[0] = image.Still;
            float total = 0f;
            for (int i = 0; i < image.GifFrames.Count; i++)
            {
                total += Mathf.Max(0.02f, image.GifFrames[i].Delay);
                image.GifFrameEnds[i] = total;
            }
            image.GifDuration = Mathf.Max(0.02f, total);
        }

        private static void ReleaseFramePixelsIfComplete(GalleryImage image)
        {
            if (image.GifFrames == null || image.GifTextures == null)
                return;
            for (int i = 0; i < image.GifTextures.Length; i++)
            {
                if (image.GifTextures[i] == null)
                    return;
            }
            image.GifFrames = null;
        }

        private static Texture2D LimitTextureSize(Texture2D source)
        {
            if (source == null)
                return null;
            int width = source.width;
            int height = source.height;
            if (width <= MaxStillTextureSize && height <= MaxStillTextureSize)
                return source;

            float scale = Mathf.Min((float)MaxStillTextureSize / width,
                (float)MaxStillTextureSize / height);
            int targetWidth = Mathf.Max(1, Mathf.FloorToInt(width * scale));
            int targetHeight = Mathf.Max(1, Mathf.FloorToInt(height * scale));
            Color32[] sourcePixels;
            try
            {
                sourcePixels = source.GetPixels32();
            }
            catch (Exception)
            {
                return source;
            }

            Color32[] targetPixels = new Color32[targetWidth * targetHeight];
            for (int y = 0; y < targetHeight; y++)
            {
                int sourceY = Mathf.Min(height - 1, y * height / targetHeight);
                int sourceRow = sourceY * width;
                int targetRow = y * targetWidth;
                for (int x = 0; x < targetWidth; x++)
                {
                    int sourceX = Mathf.Min(width - 1, x * width / targetWidth);
                    targetPixels[targetRow + x] = sourcePixels[sourceRow + sourceX];
                }
            }

            Texture2D target = new Texture2D(targetWidth, targetHeight,
                TextureFormat.RGBA32, false);
            target.SetPixels32(targetPixels);
            target.filterMode = FilterMode.Bilinear;
            target.Apply(false, true);
            UnityEngine.Object.Destroy(source);
            return target;
        }

        private static Texture2D CreateTexture(GifDecoder.GifFrame frame)
        {
            if (frame == null || frame.Pixels == null || frame.Width <= 0 || frame.Height <= 0)
                return null;
            byte[] pixels = frame.Pixels;
            Color32[] colors = new Color32[frame.Width * frame.Height];
            for (int i = 0; i < colors.Length; i++)
            {
                int offset = i * 4;
                colors[i] = new Color32(pixels[offset], pixels[offset + 1],
                    pixels[offset + 2], pixels[offset + 3]);
            }

            Texture2D texture = new Texture2D(frame.Width, frame.Height,
                TextureFormat.RGBA32, false);
            texture.SetPixels32(colors);
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply(false, true);
            return texture;
        }

        private static async Task LoadAsync(ulong workshopId, GalleryEntry entry)
        {
            try
            {
                string pageUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + workshopId;
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, pageUrl))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 RimWorld Mod Manager");
                    using (HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        List<string> imageUrls = ExtractImageUrls(html);
                        List<Task<byte[]>> downloads = imageUrls.Take(MaxPreviewImages)
                            .Select(url => DownloadImageAsync(url)).ToList();
                        byte[][] downloadsResult = await Task.WhenAll(downloads).ConfigureAwait(false);
                        List<GalleryImage> images = new List<GalleryImage>();
                        for (int i = 0; i < downloadsResult.Length; i++)
                        {
                            GalleryImage image = CreateGalleryImage(downloadsResult[i]);
                            if (image != null)
                                images.Add(image);
                        }
                        entry.Images = images.ToArray();
                    }
                }
                if (entry.Images == null || entry.Images.Length == 0)
                    entry.Error = "No preview images were found.";
            }
            catch (Exception exception)
            {
                entry.Error = exception.Message;
            }
            finally
            {
                entry.Loading = false;
            }
        }

        private static GalleryImage CreateGalleryImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;
            GalleryImage image = new GalleryImage();
            if (GifDecoder.HasGifHeader(bytes))
            {
                string error;
                List<GifDecoder.GifFrame> frames = GifDecoder.Decode(bytes,
                    MaxGifFrames, MaxGifWidth, MaxGifHeight, MaxGifPixels, out error);
                if (frames.Count > 0)
                {
                    image.GifFrames = frames;
                    return image;
                }
                image.StillFailed = true;
                return image;
            }
            image.Bytes = bytes;
            return image;
        }

        private static List<string> ExtractImageUrls(string html)
        {
            List<string> urls = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection matches = UrlRegex.Matches(html ?? string.Empty);
            for (int i = 0; i < matches.Count && urls.Count < MaxPreviewImages; i++)
            {
                string url = matches[i].Value.Replace("\\/", "/").Replace("&amp;", "&");
                if (url.IndexOf("images.steamusercontent.com/ugc/",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    url.IndexOf("steamuserimages-a.akamaihd.net/ugc/",
                        StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string normalized = NormalizeImageUrl(url);
                if (string.IsNullOrEmpty(normalized) || !seen.Add(normalized))
                    continue;
                urls.Add(normalized);
            }
            return urls;
        }

        private static string NormalizeImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;
            int queryIndex = url.IndexOf('?');
            if (queryIndex >= 0)
                url = url.Substring(0, queryIndex);
            int fragmentIndex = url.IndexOf('#');
            if (fragmentIndex >= 0)
                url = url.Substring(0, fragmentIndex);
            return url.TrimEnd('&');
        }

        private static async Task<byte[]> DownloadImageAsync(string url)
        {
            try
            {
                byte[] bytes = await Client.GetByteArrayAsync(url).ConfigureAwait(false);
                return bytes.Length <= MaxPreviewImageBytes ? bytes : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TrimCache(ulong keepWorkshopId)
        {
            if (Entries.Count <= MaxCachedEntries)
                return;
            foreach (KeyValuePair<ulong, GalleryEntry> pair in Entries)
            {
                if (pair.Key == keepWorkshopId)
                    continue;
                GalleryEntry removed;
                if (Entries.TryRemove(pair.Key, out removed))
                    ReleaseTextures(removed);
                if (Entries.Count <= MaxCachedEntries)
                    break;
            }
        }

        private static void ReleaseTextures(GalleryEntry entry)
        {
            if (entry == null || entry.Images == null)
                return;
            for (int i = 0; i < entry.Images.Length; i++)
            {
                GalleryImage image = entry.Images[i];
                if (image == null)
                    continue;
                if (image.Still != null)
                    UnityEngine.Object.Destroy(image.Still);
                if (image.GifTextures == null)
                    continue;
                for (int frameIndex = 0; frameIndex < image.GifTextures.Length; frameIndex++)
                {
                    Texture2D frameTexture = image.GifTextures[frameIndex];
                    if (frameTexture != null && frameTexture != image.Still)
                        UnityEngine.Object.Destroy(frameTexture);
                }
            }
        }
    }
}
