using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Lilly.IntegratedModManager
{
    internal static class WorkshopPreviewGallery
    {
        internal sealed class GalleryEntry
        {
            internal volatile bool Loading;
            internal volatile byte[][] ImageBytes;
            internal volatile string Error;
            internal List<Texture2D> Textures;
        }

        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private static readonly ConcurrentDictionary<ulong, GalleryEntry> Entries =
            new ConcurrentDictionary<ulong, GalleryEntry>();
        private static readonly Regex UrlRegex = new Regex(
            @"https?:\\?/\\?/[^\""'\s<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static GalleryEntry Request(ulong workshopId)
        {
            if (workshopId == 0UL)
                return null;
            GalleryEntry entry = Entries.GetOrAdd(workshopId, _ => new GalleryEntry());
            if (!entry.Loading && entry.ImageBytes == null && entry.Error == null)
            {
                entry.Loading = true;
                Task.Run(() => LoadAsync(workshopId, entry));
            }
            return entry;
        }

        internal static IReadOnlyList<Texture2D> GetTextures(GalleryEntry entry)
        {
            if (entry == null || entry.ImageBytes == null)
                return Array.Empty<Texture2D>();
            if (entry.Textures != null)
                return entry.Textures;

            List<Texture2D> textures = new List<Texture2D>();
            byte[][] images = entry.ImageBytes;
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] == null || images[i].Length == 0)
                    continue;
                try
                {
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(texture, images[i]))
                    {
                        texture.filterMode = FilterMode.Bilinear;
                        textures.Add(texture);
                    }
                    else
                        UnityEngine.Object.Destroy(texture);
                }
                catch (Exception)
                {
                    // Ignore unsupported or malformed workshop images.
                }
            }
            entry.Textures = textures;
            return textures;
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
                        List<Task<byte[]>> downloads = imageUrls.Select(url => DownloadImageAsync(url)).ToList();
                        byte[][] images = await Task.WhenAll(downloads).ConfigureAwait(false);
                        entry.ImageBytes = images.Where(bytes => bytes != null && bytes.Length > 0)
                            .Take(6).ToArray();
                    }
                }
                if (entry.ImageBytes.Length == 0)
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

        private static List<string> ExtractImageUrls(string html)
        {
            List<string> urls = new List<string>();
            MatchCollection matches = UrlRegex.Matches(html ?? string.Empty);
            for (int i = 0; i < matches.Count; i++)
            {
                string url = matches[i].Value.Replace("\\/", "/").Replace("&amp;", "&");
                if ((url.IndexOf("images.steamusercontent.com/ugc/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     url.IndexOf("steamuserimages-a.akamaihd.net/ugc/", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    !urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                    urls.Add(url);
            }
            return urls.Take(6).ToList();
        }

        private static async Task<byte[]> DownloadImageAsync(string url)
        {
            try
            {
                byte[] bytes = await Client.GetByteArrayAsync(url).ConfigureAwait(false);
                return bytes.Length <= 8 * 1024 * 1024 ? bytes : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
