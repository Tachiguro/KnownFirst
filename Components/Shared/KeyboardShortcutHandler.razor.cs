using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace KnownFirst.Components.Shared;

public sealed partial class KeyboardShortcutHandler : IAsyncDisposable
{
    [Inject]
    public required IJSRuntime JavaScript { get; set; }

    [Parameter]
    public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }

    private readonly string _pageId = Guid.NewGuid().ToString("N");
    private DotNetObjectReference<KeyboardShortcutHandler>? _objectReference;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _objectReference = DotNetObjectReference.Create(this);
            await JavaScript.InvokeVoidAsync("knownFirst.shortcuts.register", _pageId, _objectReference);
        }
    }

    [JSInvokable("OnKeyDown")]
    public async Task HandleKeyDownAsync(string key, string code, bool repeat)
    {
        if (OnKeyDown.HasDelegate)
        {
            await OnKeyDown.InvokeAsync(new KeyboardEventArgs { Key = key, Code = code, Repeat = repeat });
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JavaScript.InvokeVoidAsync("knownFirst.shortcuts.unregister", _pageId);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (TaskCanceledException)
        {
        }
        
        _objectReference?.Dispose();
    }
}
