using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

[Authorize] // require authenticated users (your cookie auth)
public class CallHub : Hub
{
    // username -> set of connection ids (support multiple tabs)
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> UserConnections
        = new();

    private string CurrentUser => Context.User?.Identity?.Name ?? "Unknown";

    public override Task OnConnectedAsync()
    {
        var connId = Context.ConnectionId;
        var user = CurrentUser;

        var conns = UserConnections.GetOrAdd(user, _ => new ConcurrentDictionary<string, byte>());
        conns[connId] = 0;

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var connId = Context.ConnectionId;
        var user = CurrentUser;

        if (UserConnections.TryGetValue(user, out var conns))
        {
            conns.TryRemove(connId, out _);
            if (conns.IsEmpty)
                UserConnections.TryRemove(user, out _);
        }

        return base.OnDisconnectedAsync(exception);
    }

    private List<string> GetConnectionIds(string username)
    {
        if (UserConnections.TryGetValue(username, out var conns))
            return conns.Keys.ToList();
        return new List<string>();
    }

    // --- Call flow signalling methods ---

    // Caller notifies server to send incoming call to target
    public Task CallUser(string targetUsername, string callType /* "audio" | "video" */)
    {
        var caller = CurrentUser;
        var targetIds = GetConnectionIds(targetUsername);
        if (targetIds.Any())
            return Clients.Clients(targetIds).SendAsync("IncomingCall", caller, callType);
        // if target offline, optionally notify caller
        return Clients.Caller.SendAsync("CallFailed", targetUsername, "User offline");
    }

    // Target sends accept: notify caller(s)
    public Task AcceptCall(string callerUsername)
    {
        var target = CurrentUser;
        var callerIds = GetConnectionIds(callerUsername);
        if (callerIds.Any())
            return Clients.Clients(callerIds).SendAsync("CallAccepted", target);
        return Task.CompletedTask;
    }

    // Target rejects call
    public Task RejectCall(string callerUsername)
    {
        var target = CurrentUser;
        var callerIds = GetConnectionIds(callerUsername);
        if (callerIds.Any())
            return Clients.Clients(callerIds).SendAsync("CallRejected", target);
        return Task.CompletedTask;
    }

    // Hangup either side
    public Task Hangup(string otherUsername)
    {
        var me = CurrentUser;
        var otherIds = GetConnectionIds(otherUsername);
        if (otherIds.Any())
            return Clients.Clients(otherIds).SendAsync("CallEnded", me);
        return Task.CompletedTask;
    }

    // WebRTC offer/answer/ice exchange (stringified JSON)
    public Task SendOffer(string targetUsername, string offer)
    {
        var from = CurrentUser;
        var ids = GetConnectionIds(targetUsername);
        if (ids.Any())
            return Clients.Clients(ids).SendAsync("ReceiveOffer", from, offer);
        return Task.CompletedTask;
    }

    public Task SendAnswer(string targetUsername, string answer)
    {
        var from = CurrentUser;
        var ids = GetConnectionIds(targetUsername);
        if (ids.Any())
            return Clients.Clients(ids).SendAsync("ReceiveAnswer", from, answer);
        return Task.CompletedTask;
    }

    public Task SendIceCandidate(string targetUsername, string candidate)
    {
        var from = CurrentUser;
        var ids = GetConnectionIds(targetUsername);
        if (ids.Any())
            return Clients.Clients(ids).SendAsync("ReceiveIceCandidate", from, candidate);
        return Task.CompletedTask;
    }
}
