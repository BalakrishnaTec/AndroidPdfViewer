# AndroidPdfViewer

Android PDF rendering library for .NET Android using native `PdfRenderer`.

## Features

- Native PDF rendering (`PdfRenderer`) with no `Xamarin.Android.PdfViewer` dependency.
- Multi-page viewer control: `PdfView`.
- Method-based loading: `LoadFilePath(...)`.
- Pinch zoom and double-tap zoom.
- Shared zoom across pages automatically.
- Optional shared pan across pages (`SynchronizePanAcrossPages`).
- XML usage supported (`androidpdfviewer.PdfView`).

## Quick start (recommended)

```csharp
using Android.App;
using Android.OS;
using AndroidPdfViewer;

namespace YourApp;

[Activity(Label = "Pdf Auto Load")]
public class PdfAutoLoadActivity : Activity
{
    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var filePath = await CopyAssetPdfToCacheAsync("sample.pdf");

        var pdfView = new PdfView(this)
        {
            MinZoom = 1f,
            MaxZoom = 6f,
            SynchronizePanAcrossPages = true
        };

        pdfView.LoadFilePath(filePath);
        SetContentView(pdfView);
    }

    private async Task<string> CopyAssetPdfToCacheAsync(string assetFileName)
    {
        var destinationPath = System.IO.Path.Combine(CacheDir!.AbsolutePath, assetFileName);

        await using var input = Assets!.Open(assetFileName);
        await using var output = File.Create(destinationPath);
        await input.CopyToAsync(output);

        return destinationPath;
    }
}
```

## XML declaration

Use this in your Android layout file:

```xml
<?xml version="1.0" encoding="utf-8"?>
<androidpdfviewer.PdfView
    xmlns:android="http://schemas.android.com/apk/res/android"
    android:id="@+id/pdfView"
    android:layout_width="match_parent"
    android:layout_height="match_parent" />
```

Then in your Activity/Fragment:

```csharp
var pdfView = FindViewById<PdfView>(Resource.Id.pdfView)!;
pdfView.SynchronizePanAcrossPages = true;
pdfView.LoadFilePath(filePath);
```

## Single-page advanced control

If you want to render one page manually, use `PdfDocument` + `ZoomablePdfPageView`:

```csharp
var document = PdfDocument.OpenFromFile(filePath);
await zoomablePdfPageView.LoadPageAsync(document, pageIndex: 0);
```

`pageIndex` is optional and defaults to `0`.

## All-pages manual adapter usage

If you want to host your own `RecyclerView`:

```csharp
recyclerView.SetAdapter(new PdfPageRecyclerAdapter(
    document,
    minZoom: 1f,
    maxZoom: 6f,
    synchronizePan: true));
```

## API summary

### `PdfView`

- `MinZoom` / `MaxZoom`
- `SynchronizePanAcrossPages`
- `LoadFilePath(string? filePath)`
- `SetPageIndicatorTextColor(Color color)`

### `ZoomablePdfPageView`

- `MinZoom` / `MaxZoom`
- `DoubleTapZoom`
- `LoadPageAsync(PdfDocument document, int pageIndex = 0, ...)`
- `SetZoomAsync(...)`, `ZoomInAsync(...)`, `ZoomOutAsync(...)`

### `PdfDocument`

- `OpenFromFile(string filePath)`
- `OpenFromStreamAsync(...)`
- `OpenFromBytesAsync(...)`
- `RenderPageAsync(...)`

## Asset setup

- Place your PDF (for example `sample.pdf`) under your app project's `Assets/` folder.
- Set build action to `AndroidAsset`.
