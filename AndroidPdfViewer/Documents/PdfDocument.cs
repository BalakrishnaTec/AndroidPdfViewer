using Android.Content;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidPdfViewer;

public sealed class PdfDocument : IDisposable
{
    private readonly PdfRenderer _renderer;
    private readonly ParcelFileDescriptor _fileDescriptor;
    private readonly Java.IO.File? _tempFile;
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private bool _disposed;

    private PdfDocument(ParcelFileDescriptor fileDescriptor, PdfRenderer renderer, Java.IO.File? tempFile)
    {
        _fileDescriptor = fileDescriptor;
        _renderer = renderer;
        _tempFile = tempFile;
    }

    public int PageCount => _renderer.PageCount;

    public int MaxBitmapSide { get; set; } = 4096;

    public static PdfDocument OpenFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }

        var file = new Java.IO.File(filePath);
        if (!file.Exists())
        {
            throw new FileNotFoundException("PDF file was not found.", filePath);
        }

        var descriptor = ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly)
            ?? throw new InvalidOperationException("Unable to open PDF file descriptor.");
        var renderer = new PdfRenderer(descriptor);
        return new PdfDocument(descriptor, renderer, tempFile: null);
    }

    public static async Task<PdfDocument> OpenFromStreamAsync(Context context, Stream pdfStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pdfStream);

        var cacheDirectory = context.CacheDir ?? throw new InvalidOperationException("Context cache directory is not available.");
        var tempPath = System.IO.Path.Combine(cacheDirectory.AbsolutePath, $"pdf-{Guid.NewGuid():N}.pdf");

        await using (var fileStream = File.Create(tempPath))
        {
            await pdfStream.CopyToAsync(fileStream, cancellationToken);
        }

        var file = new Java.IO.File(tempPath);
        var descriptor = ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly)
            ?? throw new InvalidOperationException("Unable to open PDF file descriptor.");
        var renderer = new PdfRenderer(descriptor);
        return new PdfDocument(descriptor, renderer, file);
    }

    public static Task<PdfDocument> OpenFromBytesAsync(Context context, byte[] pdfBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        var stream = new MemoryStream(pdfBytes, writable: false);
        return OpenFromStreamAsync(context, stream, cancellationToken);
    }

    public async Task<Bitmap> RenderPageAsync(
        int pageIndex,
        int viewportWidth,
        int viewportHeight,
        float zoomFactor = 1f,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (pageIndex < 0 || pageIndex >= _renderer.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), $"Page index must be between 0 and {_renderer.PageCount - 1}.");
        }

        if (viewportWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), "Viewport width must be greater than 0.");
        }

        if (viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight), "Viewport height must be greater than 0.");
        }

        var safeZoom = Math.Clamp(zoomFactor, 1f, 8f);

        await _renderLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var page = _renderer.OpenPage(pageIndex);

                var baseScale = Math.Min((float)viewportWidth / page.Width, (float)viewportHeight / page.Height);
                if (baseScale <= 0f)
                {
                    baseScale = 1f;
                }

                var renderScale = baseScale * safeZoom;
                var targetWidth = Math.Max(1, (int)Math.Round(page.Width * renderScale));
                var targetHeight = Math.Max(1, (int)Math.Round(page.Height * renderScale));

                var maxSide = Math.Max(512, MaxBitmapSide);
                var longestSide = Math.Max(targetWidth, targetHeight);
                if (longestSide > maxSide)
                {
                    var downScale = (float)maxSide / longestSide;
                    targetWidth = Math.Max(1, (int)Math.Round(targetWidth * downScale));
                    targetHeight = Math.Max(1, (int)Math.Round(targetHeight * downScale));
                }

                var bitmap = Bitmap.CreateBitmap(targetWidth, targetHeight, Bitmap.Config.Argb8888!);
                bitmap.EraseColor(Color.White);

                using var matrix = new Matrix();
                matrix.SetScale((float)targetWidth / page.Width, (float)targetHeight / page.Height);
                page.Render(bitmap, null, matrix, PdfRenderMode.ForDisplay);

                return bitmap;
            }, cancellationToken);
        }
        finally
        {
            _renderLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _renderer.Dispose();
        _fileDescriptor.Dispose();
        _renderLock.Dispose();

        if (_tempFile is not null && _tempFile.Exists())
        {
            _tempFile.Delete();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PdfDocument));
        }
    }
}
