using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Widget;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidPdfViewer;

[Android.Runtime.Register("androidpdfviewer.PdfView")]
public class PdfView : FrameLayout
{
    private readonly RecyclerView _recyclerView;
    private readonly LinearLayoutManager _layoutManager;
    private readonly TextView _pageIndicator;
    private readonly RecyclerView.OnScrollListener _pageScrollListener;

    private CancellationTokenSource? _loadCancellationTokenSource;
    private PdfDocument? _document;
    private string? _filePath;

    public PdfView(Context context) : this(context, null)
    {
    }

    public PdfView(Context context, IAttributeSet? attrs) : this(context, attrs, 0)
    {
    }

    public PdfView(Context context, IAttributeSet? attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
        _layoutManager = new LinearLayoutManager(context);

        _recyclerView = new PinchFriendlyRecyclerView(context)
        {
            LayoutParameters = new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent),
            VerticalScrollBarEnabled = true,
            ScrollBarStyle = ScrollbarStyles.InsideOverlay
        };

        _recyclerView.SetLayoutManager(_layoutManager);
        AddView(_recyclerView);

        _pageIndicator = new TextView(context)
        {
            LayoutParameters = new LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Start
            },
            Visibility = ViewStates.Gone
        };
        _pageIndicator.SetPadding(16, 8, 16, 8);
        AddView(_pageIndicator);

        _pageScrollListener = new PageScrollListener(this);
        _recyclerView.AddOnScrollListener(_pageScrollListener);
    }

    public float MinZoom { get; set; } = 1f;

    public float MaxZoom { get; set; } = 6f;

    public bool SynchronizePanAcrossPages { get; set; }

    public void LoadFilePath(string? filePath)
    {
        if (string.Equals(_filePath, filePath, StringComparison.Ordinal))
        {
            return;
        }

        _filePath = filePath;
        _ = LoadFromFilePathAsync(filePath);
    }

    private async Task LoadFromFilePathAsync(string? filePath, CancellationToken cancellationToken = default)
    {
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        _loadCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _loadCancellationTokenSource.Token;

        _document?.Dispose();
        _document = null;
        _recyclerView.SetAdapter(null);
        UpdatePageIndicator(-1, 0);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        token.ThrowIfCancellationRequested();

        var loadedDocument = await Task.Run(() => PdfDocument.OpenFromFile(filePath), token);

        if (token.IsCancellationRequested)
        {
            loadedDocument.Dispose();
            return;
        }

        _document = loadedDocument;
        _recyclerView.SetAdapter(new PdfPageRecyclerAdapter(_document, MinZoom, MaxZoom, SynchronizePanAcrossPages));
        UpdatePageIndicator(0, _document.PageCount);
    }

    private void UpdatePageIndicator(int pageIndex, int pageCount)
    {
        if (pageCount <= 0 || pageIndex < 0)
        {
            _pageIndicator.Visibility = ViewStates.Gone;
            _pageIndicator.Text = string.Empty;
            return;
        }

        var safePage = Math.Min(Math.Max(pageIndex, 0), pageCount - 1);
        _pageIndicator.Text = $"{safePage + 1}/{pageCount}";
        _pageIndicator.Visibility = ViewStates.Visible;
    }

    public void SetPageIndicatorTextColor(Color color)
    {
        _pageIndicator.SetTextColor(color);
    }

    private sealed class PageScrollListener : RecyclerView.OnScrollListener
    {
        private readonly PdfView _owner;

        public PageScrollListener(PdfView owner)
        {
            _owner = owner;
        }

        public override void OnScrolled(RecyclerView recyclerView, int dx, int dy)
        {
            base.OnScrolled(recyclerView, dx, dy);

            var total = _owner._document?.PageCount ?? 0;
            var first = _owner._layoutManager.FindFirstVisibleItemPosition();
            _owner.UpdatePageIndicator(first, total);
        }
    }

    private sealed class PinchFriendlyRecyclerView : RecyclerView
    {
        public PinchFriendlyRecyclerView(Context context) : base(context) { }

        public override bool OnInterceptTouchEvent(MotionEvent? e)
        {
            if ((e?.PointerCount ?? 0) > 1)
            {
                return false; // never intercept pinch
            }

            return base.OnInterceptTouchEvent(e);
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if ((e?.PointerCount ?? 0) > 1)
            {
                return false; // do not consume pinch
            }

            return base.OnTouchEvent(e);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource?.Dispose();
            _loadCancellationTokenSource = null;

            _recyclerView.RemoveOnScrollListener(_pageScrollListener);
            _recyclerView.SetAdapter(null);

            _document?.Dispose();
            _document = null;
        }

        base.Dispose(disposing);
    }
}
