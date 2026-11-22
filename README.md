# Lilly Integrated Mod Manager

RimWorld 1.7 mod replacing the vanilla Mods menu page with a category- and tag-based manager.

## Current features
- Four categories: Pre/Core/Normal/After-load.
- Category activation state and per-mod activation state.
- Ctrl-click, Shift-click, and Ctrl+A multi-selection.
- Move selected mods between categories.
- Category-specific folders with create, rename, collapse, move, and delete operations.
- Drag-and-drop category/folder moves, folder ordering, and multi-mod reordering.
- User-defined tags that can be assigned to multiple mods, with tag-specific folders.
- Toggle activation for all eligible mods in a tag, and add or remove tags from selected mods.
- Aggregate active, inactive, and mixed state controls for tags and folders.
- Save, load, and delete named presets containing categories, folders, tags, and activation choices.
- Search by mod name, display name, PackageId, author, description, version, tags, and category.
- Three-column quick folder mover with paging, per-column scrolling, search, favorites, and recent targets.
- Steam Workshop unsubscribe requests, separate management-list removal, and confirmed local mod-folder deletion.
- Protected Core and official DLC activation.
- Apply changes to the vanilla mod configuration; cancel unapplied changes.
- Persistent category/tag/folder assignments, collapsed state, and activation choices.
- Tag definitions, tag folders, and tag assignments are saved immediately and remain when unapplied manager changes are canceled.
- Favorite and recent quick-move folders persist in ModSettings.

Newly discovered mods are added to the Normal category. Core and official DLC are protected from moving or disabling. Changes apply on the next game restart.

Loading a preset replaces the current edit draft; it does not change the active mod configuration until Apply is selected. Installed mods missing from an older preset are retained in a default category.

Automatic sorting uses available mod dependency metadata while preserving the existing order where dependencies do not constrain it. Apply checks the resulting RimWorld load order and asks before applying when it finds ordering problems. Core and official DLC remain protected. Removing a mod from the manager is separate from deleting its files; Workshop unsubscribe requests do not erase its saved management data.

## Standalone Windows app

Build scripts are separated: `build.bat` builds only the RimWorld mod, while `build-exe.bat` publishes only the standalone Windows desktop app. The EXE is written to `Desktop\\publish\\win-x64\\Lilly.ModManager.exe`; its Windows icon is embedded during publishing. A Windows GitHub Actions workflow also publishes the standalone x64 EXE when `Desktop/**` changes; the build artifact is attached to the workflow run.

The desktop app uses a game-style three-pane layout: category/tag navigation on the left, a folder-aware mod list in the center, and the selected mod's information, supported game versions, clickable project and Workshop links, and local preview image on the right. The visible list is rendered as selectable mod rows, not a spreadsheet/table. It supports category and tag views, active/mixed activation controls, alternating row backgrounds, colored tag chips with automatic foreground contrast (one line only, with overflow shown in the row tooltip), collapsible folders, Ctrl/Shift multi-selection and keyboard navigation, drag-and-drop assignment to categories/tags/folders, drag-and-drop load-order changes, drag reordering of categories and folders, and moving category folders between categories. The toolbar filters by activation, source, and issue type (missing required mods, unsupported game version, invalid load order, or missing installation folders). Automatic `신규` and `변경됨` tags follow the in-game behavior: the first state scan establishes a baseline without tagging every existing mod, newly installed mods are tagged only after that baseline, and version changes are tagged once. The **폴더 바로가기** dialog displays the folder groups in columns and supports search, favorites, and recent targets; it uses the same `category:<id>:<folderId>` and `tag:<id>:<folderId>` keys as the in-game manager so those shortcuts are shared. It can scan local, official, and Steam Workshop mod folders; filter by source, activation state, and detected issue type; search installed-mod metadata; enable or disable one or multiple mods; change load order manually or sort by declared dependencies; inspect missing requirements, unsupported game versions, and load-order problems before applying; assign categories, tags, and folders; edit category, tag, and mod colors; customize displayed mod names; toggle automatic `신규` and `변경됨` tags; save, load, and delete manager presets; discard unapplied activation changes by reloading saved settings; remove inactive mods from the management list without deleting files; delete confirmed local mod folders; open mod folders and Workshop pages; and launch RimWorld. Missing active mod entries are retained rather than silently dropped. It reads and writes the same RimWorld `ModsConfig.xml`, the in-game manager's `ModSettings` data, and the manager preset XML files. Existing configuration files are backed up before writing. Close RimWorld before applying a desktop configuration. Steam subscription/unsubscription and Workshop publishing are intentionally not included in the standalone app.

The existing in-game manager remains in place. On startup, it creates a desktop shortcut when `Lilly.ModManager.exe` is present beside the mod's `build.bat`. A **Create desktop shortcut** button is also available in the in-game manager options window.

## Build

Run `build.bat` on Windows with the .NET SDK 10 installed. Since `LoadFolders.xml` loads the mod root for RimWorld 1.7, the output is written to `Assemblies\Lilly.IntegratedModManager.dll`.

## 한국어 안내

### 프로젝트 소개

**Lilly Integrated Mod Manager**는 RimWorld 1.7용 모드 관리 모드입니다. 게임의 기본 모드 메뉴를 카테고리와 태그 중심의 관리 화면으로 교체합니다. 별도로 실행할 수 있는 Windows 데스크톱 앱도 함께 제공합니다.

### 게임 내 모드 관리자

- 모드를 Pre / Core / Normal / After-load 네 가지 카테고리로 분류합니다.
- 카테고리 및 개별 모드의 활성화 상태를 관리합니다.
- Ctrl, Shift, Ctrl+A로 여러 모드를 선택하고 한꺼번에 이동하거나 정렬할 수 있습니다.
- 카테고리별 폴더와 태그별 폴더를 만들고 이름 변경, 접기, 이동 및 삭제를 할 수 있습니다.
- 사용자 태그를 만들고 여러 모드에 지정할 수 있습니다.
- 태그와 폴더의 전체 활성화·비활성화 및 혼합 상태를 확인할 수 있습니다.
- 카테고리, 폴더, 태그 및 활성화 설정을 포함한 프리셋을 저장·불러오기·삭제할 수 있습니다.
- 이름, 표시 이름, PackageId, 제작자, 설명, 버전, 태그 및 카테고리로 검색할 수 있습니다.
- 빠른 폴더 이동 창에서 검색, 즐겨찾기, 최근 대상 및 여러 열을 이용해 모드를 이동할 수 있습니다.
- Steam 구독 해제 요청, 관리 목록에서 제거, 로컬 모드 폴더 삭제를 구분해 처리합니다.
- Core와 공식 DLC는 이동 또는 비활성화로부터 보호합니다.
- 변경 사항을 적용하거나 적용하지 않은 변경 사항을 취소할 수 있습니다.
- 의존성 정보를 바탕으로 모드를 정렬하고, 적용 전에 로드 순서 문제를 검사합니다.

새로 발견한 모드는 Normal 카테고리에 추가됩니다. 프리셋을 불러오면 현재 편집 중인 초안이 교체되지만, **적용**을 누르기 전까지 게임의 활성 모드 설정은 바뀌지 않습니다. 이전 프리셋에 포함되지 않은 설치된 모드는 기본 카테고리에 유지됩니다.

### Windows 데스크톱 앱

데스크톱 앱은 게임 내 관리자와 유사한 3개 영역으로 구성됩니다.

- **왼쪽:** 카테고리 및 태그 탐색
- **가운데:** 폴더를 포함한 모드 목록
- **오른쪽:** 선택한 모드의 정보, 지원 게임 버전, 프로젝트·Steam Workshop 링크 및 로컬 미리보기 이미지

스프레드시트가 아닌 선택 가능한 모드 행으로 목록을 표시합니다. 교차 행 배경색, 태그 색상과 글자색 자동 대비, 폴더 접기, 키보드 탐색, 다중 선택 및 드래그 앤 드롭을 지원합니다.

주요 기능:
- 카테고리 및 태그별 모드 탐색
- 활성화 상태, 모드 출처 및 문제 유형별 필터링
- 모드 정보 검색 및 상세 정보 확인
- 모드 한 개 또는 여러 개의 활성화·비활성화
- 로드 순서 수동 변경 및 의존성 기반 자동 정렬
- 누락된 필수 모드, 지원하지 않는 게임 버전, 잘못된 로드 순서 및 설치 폴더 누락 확인
- 카테고리·태그·폴더 지정 및 색상 편집
- 모드 표시 이름 사용자 지정
- 자동 '신규' 및 '변경됨' 태그 관리
- 프리셋 저장·불러오기·삭제
- 적용하지 않은 활성화 변경 사항 취소
- 관리 목록에서 모드 제거 및 확인 절차를 거친 로컬 폴더 삭제
- 모드 폴더와 Steam Workshop 페이지 열기
- RimWorld 실행
- 게임 내 관리자와 동일한 설정 및 프리셋 데이터 공유

데스크톱 앱은 RimWorld의 `ModsConfig.xml`, 게임 내 관리자의 `ModSettings` 데이터 및 관리자 프리셋 XML을 읽고 씁니다. 설정 파일을 쓰기 전에 기존 파일을 백업합니다. 데스크톱 앱에서 설정을 적용하기 전에 RimWorld를 종료하세요.

데스크톱 앱에서는 Steam 구독/구독 해제 및 Workshop 업로드 기능을 제공하지 않습니다. 게임 내 모드 관리자는 별도로 계속 사용할 수 있습니다.

### 빌드 방법

Windows에서 .NET SDK 10을 설치한 다음 프로젝트 루트에서 원하는 배치 파일을 실행합니다.

- `build.bat`: RimWorld 모드 DLL만 빌드합니다. 결과 파일은 `Assemblies\\Lilly.IntegratedModManager.dll`입니다.
- `build-exe.bat`: Windows x64 데스크톱 앱을 단독 실행 파일로 게시합니다. 결과 파일은 `Desktop\\publish\\win-x64\\Lilly.ModManager.exe`입니다.

데스크톱 앱의 Windows 아이콘은 게시 과정에서 실행 파일에 포함됩니다. GitHub Actions에서도 `Desktop/**` 변경 시 Windows x64 실행 파일을 빌드하며, 결과물은 해당 Workflow 실행의 아티팩트로 제공됩니다.

게임 내 관리자는 모드의 `build.bat`과 같은 위치에 `Lilly.ModManager.exe`가 있으면 시작할 때 데스크톱 바로가기를 생성합니다. 게임 내 관리자 옵션 창에서도 **Create desktop shortcut** 버튼으로 바로가기를 만들 수 있습니다.
