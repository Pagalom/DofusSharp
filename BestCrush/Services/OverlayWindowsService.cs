using BestCrush.Components.Overlays;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using Microsoft.Extensions.DependencyInjection;
#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using System.Runtime.InteropServices;
#endif

namespace BestCrush.Services;

public sealed class OverlayWindowView
{
    public required string Id { get; init; }
    public required WorkspaceOverlayKind Kind { get; init; }
    public required string Title { get; init; }
    public OverlayPopup? Popup { get; set; }
    public bool Pinned { get; set; }
    public bool Visible { get; set; }
    public bool RequestedVisible { get; set; }
    internal bool ContextVisible(bool context) => RequestedVisible && (Kind!=WorkspaceOverlayKind.Popup || Pinned || context);
    internal bool ParentHovered, Hovered;
    internal string? ParentId;
    internal CancellationTokenSource? LayoutSave;
    internal Window? Window;
    internal OverlayWindowLayout? MotionStart;
    internal int MotionEdge;
    internal double MotionX,MotionY,MotionScale;
#if WINDOWS
    internal AppWindow? App;
    internal IntPtr Handle;
    internal IDispatcherTimer? MotionTimer;
#endif
}

/// <summary>Shared native chrome for six overlay views and independent pinned detail windows.</summary>
public sealed class OverlayWindowsService(OverlayWorkspaceService workspace,
    OverlayLayoutSettingsService layouts,DofusWindowService? gameWindows=null)
{
    private readonly Dictionary<string,OverlayWindowView> _windows=[];
    private readonly HashSet<string> _hidden=[];
    private CancellationTokenSource? _hover;
    private string? _hoverKey;
    private IDispatcherTimer? _visibilityTimer;
    public event Action? Changed;
    public Action? OpenMain { get; set; }
    public IReadOnlyCollection<OverlayWindowView> Windows => _windows.Values;
    public OverlayWindowView? Get(string id) => _windows.GetValueOrDefault(id);
    public bool ContainsScreenPoint(int x,int y)
    {
#if WINDOWS
        return _windows.Values.Any(w=>w.Visible && w.App is { } a && x>=a.Position.X && y>=a.Position.Y && x<a.Position.X+a.Size.Width && y<a.Position.Y+a.Size.Height);
#else
        return false;
#endif
    }
    public bool IsVisible(WorkspaceOverlayKind kind) => _windows.Values.Any(w=>w.Kind==kind && w.Visible);
    public void Initialize()
    {
        workspace.ShowRequested -= Show; workspace.ShowRequested += Show;
        workspace.Changed -= RefreshCurrentPopups;workspace.Changed += RefreshCurrentPopups;
        workspace.ModuleRequested += _ => Dispatch(() => OpenMain?.Invoke());
        workspace.Initialize();Show(WorkspaceOverlayKind.Control);
        _visibilityTimer=Application.Current?.Dispatcher.CreateTimer();
        if(_visibilityTimer is not null){_visibilityTimer.Interval=TimeSpan.FromMilliseconds(250);_visibilityTimer.Tick+=(_,_)=>RefreshContextVisibility();_visibilityTimer.Start();}
    }
    public void Show(WorkspaceOverlayKind kind) => Dispatch(()=>
    {
        var id=kind.ToString();
        if(!_windows.TryGetValue(id,out var view))
        {
            view=new(){Id=id,Kind=kind,Title=Title(kind)};_windows.Add(id,view);
            Create(view);
        }
        ShowWindow(view,true);
        workspace.RefreshModule(kind);
    });
    private void RefreshCurrentPopups() => Dispatch(()=>
    {
        bool changed=false;
        foreach(var window in _windows.Values.Where(w=>w.Kind==WorkspaceOverlayKind.Popup).ToArray())
        {
            if(window.Popup is not { } popup)continue;
            var updated=ResolvePopup(popup);
            if(!ReferenceEquals(updated,popup)){window.Popup=updated;changed=true;}
        }
        if(changed)Changed?.Invoke();
    });
    internal OverlayPopup ResolvePopup(OverlayPopup popup)
    {
        if(popup.MarketItemId is {} id && popup.MarketGroup is {} group)
        {
            if(!string.Equals(popup.MarketServer,workspace.EquipmentSearchServer,StringComparison.OrdinalIgnoreCase) ||
                popup.TargetWindow!=workspace.TargetWindow)return popup;
            if(workspace.EquipmentSearch.Indicators.TryGetValue(id,out var indicators) && indicators.Loaded &&
                indicators.Groups.FirstOrDefault(indicator=>indicator.Key==group) is {} indicator)
                return WithLines(popup,indicator.Lines);
            return popup;
        }
        if(workspace.Profitability is not {} current)return popup;
        if(popup.Comparison is {} previous && previous.FocusId==current.FocusId && !ReferenceEquals(previous,current))
            return popup with{Comparison=current};
        if(popup.Key==$"purchase:{current.FocusId}" || popup.Key==$"purchase:{current.FocusId}:sale")
            return WithLines(popup,current.PurchaseDetails.Count>0?current.PurchaseDetails:current.PurchaseChecks);
        if(popup.Key==$"craft:{current.FocusId}")return WithLines(popup,current.Ingredients);
        if(popup.CheckRoute is {} route && popup.Key==$"checks:{current.FocusId}:{route}")
            return WithLines(popup,current.Checks(route));
        return popup;
    }
    private static OverlayPopup WithLines(OverlayPopup popup,IReadOnlyList<OverlayPriceLine> lines) =>
        ReferenceEquals(popup.Lines,lines) || popup.Lines.SequenceEqual(lines) ? popup : popup with{Lines=lines};
    public void Toggle(WorkspaceOverlayKind kind)
    { if(IsVisible(kind)) Hide(kind.ToString());else Show(kind); }
    public void Hide(string id) => Dispatch(()=>
    {
        if(_windows.TryGetValue(id,out var view)) ShowWindow(view,false);
    });
    public void ToggleAll() => Dispatch(()=>
    {
        var visible=_windows.Values.Where(w=>w.Visible && w.Kind!=WorkspaceOverlayKind.Control).ToArray();
        if(visible.Length>0)
        { _hidden.Clear();foreach(var w in visible){_hidden.Add(w.Id);ShowWindow(w,false);} }
        else {foreach(var id in _hidden.ToArray())if(_windows.TryGetValue(id,out var w))ShowWindow(w,true);_hidden.Clear();}
    });
    public void TogglePin(string id)
    { if(Get(id) is { Kind:WorkspaceOverlayKind.Popup } w){w.Pinned=!w.Pinned;RefreshContextVisibility();Changed?.Invoke();} }
    public void HoverPopup(string parent, OverlayPopup popup)
    {
        popup=ResolvePopup(popup);
        _hover?.Cancel();_hover?.Dispose();_hover=new();var token=_hover.Token;
        _hoverKey=popup.Key;
        if(Get(popup.Key) is { } existing){existing.ParentHovered=true;existing.Popup=popup;RepositionPopup(existing,parent);Changed?.Invoke();return;}
        _=OpenAfterDelayAsync(parent,popup,token);
    }
    private async Task OpenAfterDelayAsync(string parent,OverlayPopup popup,CancellationToken token)
    {
        try{await Task.Delay(800,token);await MainThread.InvokeOnMainThreadAsync(()=>OpenPopup(parent,popup));}
        catch(OperationCanceledException){}
    }
    public void OpenPopup(string parent,OverlayPopup popup)
    {
        popup=ResolvePopup(popup);
        if(_windows.TryGetValue(popup.Key,out var existing))
        {existing.Popup=popup;existing.ParentHovered=true;RepositionPopup(existing,parent);ShowWindow(existing,true);return;}
        // Keep pinned popups intact; an ordinary hover opens one transient popup at a time.
        foreach(var transient in _windows.Values.Where(w=>w.Kind==WorkspaceOverlayKind.Popup && !w.Pinned).ToArray())Close(transient.Id);
        var view=new OverlayWindowView{Id=popup.Key,Kind=WorkspaceOverlayKind.Popup,Title=popup.Title,Popup=popup,ParentHovered=true,ParentId=parent};
        workspace.RetainIndicatorPopup(popup);
        _windows.Add(view.Id,view);Create(view,parent);ShowWindow(view,true);
    }
    public void LeavePopup(string key)
    {
        if(_hoverKey==key){_hover?.Cancel();_hoverKey=null;}
        if(Get(key) is { } view){view.ParentHovered=false;_=CloseAfterLeaveAsync(view);}
    }
    public void Pointer(string id,bool inside)
    {if(Get(id) is { } view){view.Hovered=inside;if(!inside)_=CloseAfterLeaveAsync(view);}}
    private async Task CloseAfterLeaveAsync(OverlayWindowView view)
    {
        await Task.Delay(400);
        await MainThread.InvokeOnMainThreadAsync(()=>{
            if(view.Kind==WorkspaceOverlayKind.Popup && !view.Pinned && !view.Hovered && !view.ParentHovered)Close(view.Id);
        });
    }
    public void Close(string id)
    {
        if(!_windows.TryGetValue(id,out var view))return;
        if(view.Kind!=WorkspaceOverlayKind.Popup){Hide(id);return;}
        if(view.Popup is {} popup)workspace.ReleaseIndicatorPopup(popup);
        SaveLayoutNow(view);_windows.Remove(id);if(view.Window is { } window) Application.Current?.CloseWindow(window);Changed?.Invoke();
    }
    public bool StartMotion(string id,int edge,double x,double y,double scale)
    {
#if WINDOWS
        if(Get(id) is not {App:{ } app,Window:{ } window} view)return false;
        var cursor=new CursorPoint{X=(int)Math.Round(x*scale),Y=(int)Math.Round(y*scale)};
        if(!ClientToScreen(view.Handle,ref cursor))return false;
        view.MotionTimer?.Stop();
        view.MotionStart=new(app.Position.X,app.Position.Y,app.Size.Width,app.Size.Height);
        view.MotionEdge=edge;view.MotionX=cursor.X;view.MotionY=cursor.Y;view.MotionScale=1;
        // Sample the native cursor directly. A WebView interop call for every mouse event
        // queues stale positions, especially while moving a layered WebView window.
        view.MotionTimer=window.Dispatcher.CreateTimer();
        view.MotionTimer.Interval=TimeSpan.FromMilliseconds(16);
        view.MotionTimer.Tick+=(_,_)=>{
            if((GetAsyncKeyState(1)&0x8000)==0){FinishMotion(id);return;}
            if(GetCursorPos(out var point))MoveMotion(id,point.X,point.Y);
        };
        view.MotionTimer.Start();return true;
#else
        return false;
#endif
    }
    public void MoveMotion(string id,double x,double y) => Dispatch(()=>
    {
#if WINDOWS
        if(Get(id) is not {App:{ } app,MotionStart:{ } start} view)return;
        int dx=(int)Math.Round(x-view.MotionX),dy=(int)Math.Round(y-view.MotionY);
        int left=start.X,top=start.Y,width=start.Width,height=start.Height;
        if(view.MotionEdge==2){left+=dx;top+=dy;}
        else
        {
            bool west=view.MotionEdge is 10 or 13 or 16,east=view.MotionEdge is 11 or 14 or 17;
            bool north=view.MotionEdge is 12 or 13 or 14,south=view.MotionEdge is 15 or 16 or 17;
            width=Math.Max(MinimumWidth(view.Kind),start.Width+(east?dx:west?-dx:0));
            height=Math.Max(MinimumHeight(view.Kind),start.Height+(south?dy:north?-dy:0));
            if(west)left=start.X+start.Width-width;if(north)top=start.Y+start.Height-height;
        }
        var bounds=layouts.ConstrainToVisibleScreen(new(left,top,width,height),MinimumWidth(view.Kind),MinimumHeight(view.Kind));
        if(view.MotionEdge==2 && (app.Position.X!=bounds.X || app.Position.Y!=bounds.Y))
            SetWindowPos(view.Handle,IntPtr.Zero,bounds.X,bounds.Y,0,0,0x0001|0x0004|0x0010|0x0200);
        else if(app.Position.X!=bounds.X || app.Position.Y!=bounds.Y || app.Size.Width!=bounds.Width || app.Size.Height!=bounds.Height)
            app.MoveAndResize(new RectInt32(bounds.X,bounds.Y,bounds.Width,bounds.Height));
#endif
    });
    public void FinishMotion(string id) => Dispatch(()=>{
        if(Get(id) is not { } view)return;
#if WINDOWS
        view.MotionTimer?.Stop();view.MotionTimer=null;
        if(view.MotionStart is not null && GetCursorPos(out var point))MoveMotion(id,point.X,point.Y);
#endif
        view.MotionStart=null;SaveLayoutNow(view);
    });
    public void RestoreLayouts()
    {
        foreach(var view in _windows.Values)
        {
#if WINDOWS
            if(view.App is { } app){var l=layouts.GetDefaultLayout(LayoutKind(view));app.MoveAndResize(new RectInt32(l.X,l.Y,l.Width,l.Height));RepositionPopup(view,view.ParentId);}
#endif
        }
    }
    public void Shutdown() => Dispatch(()=>
    {
        _hover?.Cancel();_visibilityTimer?.Stop();workspace.Changed-=RefreshCurrentPopups;workspace.Dispose();
        foreach(var view in _windows.Values.ToArray()){SaveLayoutNow(view);if(view.Window is { } window)Application.Current?.CloseWindow(window);}
        _windows.Clear();
    });
    private static int MinimumWidth(WorkspaceOverlayKind kind) => kind switch { WorkspaceOverlayKind.Control => 340, WorkspaceOverlayKind.Runes => 240, _ => 320 };
    private static int MinimumHeight(WorkspaceOverlayKind kind) => kind switch { WorkspaceOverlayKind.Control => 46, WorkspaceOverlayKind.Runes => 180, _ => 180 };
    private void Create(OverlayWindowView view,string? parent=null)
    {
#if WINDOWS
        IntPtr previousForeground=GetForegroundWindow();
#endif
        var web=new BlazorWebView{HostPage="wwwroot/overlay.html",BackgroundColor=Colors.Transparent};
        web.RootComponents.Add(new RootComponent{Selector="#app",ComponentType=typeof(OverlayRoot),
            Parameters=new Dictionary<string,object?>{{nameof(OverlayRoot.WindowId),view.Id}}});
#if WINDOWS
        web.BlazorWebViewInitialized+=(_,e)=>{
            e.WebView.DefaultBackgroundColor=global::Windows.UI.Color.FromArgb(0,0,0,0);
            e.WebView.UseSystemFocusVisuals=false;
            e.WebView.FocusVisualPrimaryThickness=new Microsoft.UI.Xaml.Thickness(0);
            e.WebView.FocusVisualSecondaryThickness=new Microsoft.UI.Xaml.Thickness(0);
        };
#endif
        var layout=layouts.GetValidatedLayout(LayoutKind(view),MinimumWidth(view.Kind),
            MinimumHeight(view.Kind),true);
#if WINDOWS
        if(parent is not null && Get(parent)?.App is { } source)
            layout=layouts.PlacePopupBeside(new(source.Position.X,source.Position.Y,source.Size.Width,source.Size.Height),layout);
#endif
        var page=new ContentPage{Content=web,BackgroundColor=Colors.Transparent,Padding=0};
        var window=new Window(page){Title="BestCrush · "+view.Title,Width=layout.Width,Height=layout.Height,X=layout.X,Y=layout.Y,
            MinimumWidth=MinimumWidth(view.Kind),MinimumHeight=MinimumHeight(view.Kind)};
        view.Window=window;
        window.Created+=(_,_)=>{
#if WINDOWS
            if(window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
            {
                var handle=WinRT.Interop.WindowNative.GetWindowHandle(native);view.Handle=handle;
                var app=AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle));view.App=app;
                var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);
                presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;
                app.SetPresenter(presenter);app.MoveAndResize(new RectInt32(layout.X,layout.Y,layout.Width,layout.Height));
                native.SystemBackdrop=null;
                RemoveSystemFrame(handle);
                SetLayeredWindowAttributes(handle,0,234,2);
                app.Changed+=(_,args)=>{
                    if(view.MotionStart is null && (args.DidPositionChange || args.DidSizeChange))
                        SaveLayoutLater(view);
                };
                NativeShowWindow(handle,view.Visible?4:0);
            }
#endif
        };
#if WINDOWS
        // MAUI reapplies WS_DLGFRAME/WS_EX_WINDOWEDGE while opening and activating
        // a window. Clear them after that mapping, rather than only in Created.
        window.Activated+=(_,_)=>window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100),()=>{
            if(view.Handle!=IntPtr.Zero)RemoveSystemFrame(view.Handle);
        });
        window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),()=>{
            if(view.Handle!=IntPtr.Zero)RemoveSystemFrame(view.Handle);
        });
#endif
        window.Destroying+=(_,_)=>{
            SaveLayoutNow(view);view.LayoutSave?.Cancel();
#if WINDOWS
            view.MotionTimer?.Stop();view.MotionTimer=null;
#endif
            view.Visible=false;view.RequestedVisible=false;view.Window=null;
#if WINDOWS
            view.App=null;view.Handle=IntPtr.Zero;
#endif
            _windows.Remove(view.Id);_hidden.Remove(view.Id);
        };
        view.Visible=true;view.RequestedVisible=true;Application.Current?.OpenWindow(window);
#if WINDOWS
        // MAUI activates newly created windows. Restore the game (or previous app window)
        // only if this newly opened overlay took focus; later Show() calls never activate it.
        window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(75),()=>{
            if(previousForeground!=IntPtr.Zero && view.Handle!=IntPtr.Zero && GetForegroundWindow()==view.Handle)
                SetForegroundWindow(previousForeground);
        });
#endif
    }
    private void RepositionPopup(OverlayWindowView view,string? parent)
    {
        if(view.Kind!=WorkspaceOverlayKind.Popup || view.Pinned)return;
        view.ParentId=parent;
#if WINDOWS
        if(view.App is not { } popup || parent is null || Get(parent)?.App is not { } source)return;
        var placement=layouts.PlacePopupBeside(new(source.Position.X,source.Position.Y,source.Size.Width,source.Size.Height),
            new(popup.Position.X,popup.Position.Y,popup.Size.Width,popup.Size.Height));
        popup.MoveAndResize(new RectInt32(placement.X,placement.Y,placement.Width,placement.Height));
#endif
    }

    private void SaveLayoutLater(OverlayWindowView view)
    {
        view.LayoutSave?.Cancel();view.LayoutSave?.Dispose();view.LayoutSave=new();
        _=SaveLayoutAfterDelayAsync(view,view.LayoutSave.Token);
    }
    private async Task SaveLayoutAfterDelayAsync(OverlayWindowView view,CancellationToken token)
    {
        try
        {
            await Task.Delay(250,token);
            await MainThread.InvokeOnMainThreadAsync(()=>{
#if WINDOWS
                if(!token.IsCancellationRequested)SaveLayoutNow(view);
#endif
            });
        }
        catch(OperationCanceledException){}
    }
    private void SaveLayoutNow(OverlayWindowView view)
    {
#if WINDOWS
        view.LayoutSave?.Cancel();
        try
        {
            if(view.App is { } app)
                layouts.SaveLayout(LayoutKind(view),new(app.Position.X,app.Position.Y,app.Size.Width,app.Size.Height));
        }
        catch(Exception error) when(error is COMException or ObjectDisposedException or InvalidOperationException)
        { /* A closing native window must not interrupt application shutdown. */ }
#endif
    }
    internal static OverlayLayoutKind LayoutKind(OverlayWindowView view) => view.Popup?.LayoutKind ?? LayoutKind(view.Kind);
    private void ShowWindow(OverlayWindowView view,bool visible)
    {
        view.RequestedVisible=visible;
        SetActualVisibility(view,visible);
    }
    private void SetActualVisibility(OverlayWindowView view,bool visible)
    {
        view.Visible=visible;
        workspace.OverlayVisibilityChanged(view.Kind,visible);
#if WINDOWS
        if(view.Handle!=IntPtr.Zero)NativeShowWindow(view.Handle,visible?4:0);
#endif
        Changed?.Invoke();
    }
    private void RefreshContextVisibility()
    {
#if WINDOWS
        GetWindowThreadProcessId(GetForegroundWindow(),out var process);
        bool context=process==Environment.ProcessId || gameWindows?.ListVisibleDofusWindows().Any(w=>w.ProcessId==process)==true;
        foreach(var view in _windows.Values.ToArray())
        {
            bool visible=view.ContextVisible(context);
            if(view.Visible!=visible)SetActualVisibility(view,visible);
        }
#endif
    }
    public static string Title(WorkspaceOverlayKind kind)=>kind switch{
        WorkspaceOverlayKind.Profitability=>"Rentabilité",WorkspaceOverlayKind.Crush=>"Résultat du concassage",
        WorkspaceOverlayKind.Forgemaging=>"Forgemagie",WorkspaceOverlayKind.Breeding=>"Élevage",WorkspaceOverlayKind.Pets=>"Familiers",
        WorkspaceOverlayKind.Runes=>"Classement runes",WorkspaceOverlayKind.Journey=>"Parcours",_=>"Panneau de contrôle"};
    private static OverlayLayoutKind LayoutKind(WorkspaceOverlayKind kind)=>kind switch{
        WorkspaceOverlayKind.Profitability=>OverlayLayoutKind.Profitability,WorkspaceOverlayKind.Crush=>OverlayLayoutKind.Crush,
        WorkspaceOverlayKind.Control=>OverlayLayoutKind.ControlBar,WorkspaceOverlayKind.Forgemaging=>OverlayLayoutKind.Forgemaging,
        WorkspaceOverlayKind.Breeding=>OverlayLayoutKind.Breeding,WorkspaceOverlayKind.Pets=>OverlayLayoutKind.Pets,
        WorkspaceOverlayKind.Runes=>OverlayLayoutKind.Runes,WorkspaceOverlayKind.Popup=>OverlayLayoutKind.PopupDetails,_=>OverlayLayoutKind.Journey};
    private static void Dispatch(Action action){if(MainThread.IsMainThread)action();else MainThread.BeginInvokeOnMainThread(action);}
#if WINDOWS
    private static readonly WindowSubclassProc FrameProc=FrameMessage;
    private static IntPtr FrameMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam,nuint id,nuint data)
    {
        // The overlay supplies its own title bar and resize handles. Some WinUI
        // versions retain a raised non-client edge even with HasBorder=false.
        if(message is 0x0083 or 0x0085)return IntPtr.Zero; // NCCALCSIZE / NCPAINT
        if(message==0x0086)return (IntPtr)1; // NCACTIVATE: no system frame to repaint
        if(message==0x0082)RemoveWindowSubclass(handle,FrameProc,id);
        return DefSubclassProc(handle,message,wParam,lParam);
    }
    private static void RemoveSystemFrame(IntPtr handle)
    {
        SetWindowSubclass(handle,FrameProc,0xBC01,0);
        uint disabled=1;
        DwmSetWindowAttribute(handle,2,ref disabled,sizeof(uint));
        nint style=GetWindowLongPtr(handle,-16);
        SetWindowLongPtr(handle,-16,style & ~(nint)(0x00800000|0x00400000|0x00040000));
        nint extended=GetWindowLongPtr(handle,-20);
        SetWindowLongPtr(handle,-20,(extended & ~(nint)(0x00000100|0x00020000|0x00000001)) | (nint)0x00080000);
        uint noBorder=0xFFFFFFFE;
        DwmSetWindowAttribute(handle,34,ref noBorder,sizeof(uint));
        SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020);
    }
    private delegate IntPtr WindowSubclassProc(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam,nuint id,nuint data);
    [DllImport("comctl32.dll")]private static extern bool SetWindowSubclass(IntPtr handle,WindowSubclassProc proc,nuint id,nuint data);
    [DllImport("comctl32.dll")]private static extern bool RemoveWindowSubclass(IntPtr handle,WindowSubclassProc proc,nuint id);
    [DllImport("comctl32.dll")]private static extern IntPtr DefSubclassProc(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]private struct CursorPoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref CursorPoint point);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref uint value,int size);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint process);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(IntPtr handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(IntPtr handle,int index,nint value);
    [DllImport("user32.dll")]private static extern bool SetLayeredWindowAttributes(IntPtr handle,uint color,byte alpha,uint flags);
    [DllImport("user32.dll",EntryPoint="ShowWindow")]private static extern bool NativeShowWindow(IntPtr handle,int command);
#endif
}
