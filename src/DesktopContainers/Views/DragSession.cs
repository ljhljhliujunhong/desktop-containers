namespace DesktopContainers;

public sealed class DragSession
{
    public const string Format = "DesktopContainers.AppDrag";
    public static DragSession? Current { get; private set; }

    public bool Consumed { get; private set; }
    public bool Cancelled { get; set; }
    public string FromId { get; }
    public string AppId { get; }
    string? _toId;
    int _index;

    DragSession(string fromId, string appId)
    {
        FromId = fromId;
        AppId = appId;
    }

    public static DragSession Begin(string fromId, string appId)
    {
        var session = new DragSession(fromId, appId);
        Current = session;
        return session;
    }

    public void Consume(string fromId, string appId, string toId, int index)
    {
        if (fromId != FromId || appId != AppId) return;
        Consumed = true;
        _toId = toId;
        _index = index;
    }

    public void Complete()
    {
        try
        {
            if (Consumed && _toId != null && !Cancelled)
                AppHost.State.MoveApp(FromId, AppId, _toId, _index);
        }
        finally
        {
            if (ReferenceEquals(Current, this))
                Current = null;
        }
    }
}
