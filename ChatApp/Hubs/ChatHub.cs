using System.Collections.Concurrent;
using ChatApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Hubs
{

    [Authorize]
    public class ChatHub : Hub
    {
        private readonly AppDbContext _db;
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> UserConnections
            = new();

        public ChatHub(AppDbContext db) { _db = db; }

        private string CurrentUserName => Context.User?.Identity?.Name ?? "Unknown";

        public override async Task OnConnectedAsync()
        {
            var conn = Context.ConnectionId;
            var user = CurrentUserName;

            var conns = UserConnections.GetOrAdd(user, _ => new ConcurrentDictionary<string, byte>());
            conns[conn] = 0;

            await BroadcastUsers();
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var conn = Context.ConnectionId;
            var user = CurrentUserName;

            if (UserConnections.TryGetValue(user, out var conns))
            {
                conns.TryRemove(conn, out _);
                if (conns.IsEmpty) UserConnections.TryRemove(user, out _);
            }

            await BroadcastUsers();
            await base.OnDisconnectedAsync(exception);
        }

        private Task BroadcastUsers()
        {
            var users = UserConnections.Keys.OrderBy(x => x).ToList();
            return Clients.All.SendAsync("UsersUpdated", users);
        }

        // Public broadcast
        public async Task SendMessageToAll(string text)
        {
            var username = CurrentUserName;
            var sender = await _db.Users.SingleOrDefaultAsync(u => u.Username == username);
            var msg = new Message { FromUserId = sender!.Id, Text = text, TimestampUtc = DateTime.UtcNow };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();

            await Clients.All.SendAsync("ReceiveMessage", username, text, msg.TimestampUtc, msg.Id, null, null, null, null);
        }

        // Public file message
        public async Task SendFileToAll(string text, string fileUrl, string fileName, string fileType, long fileSize)
        {
            var username = CurrentUserName;
            var sender = await _db.Users.SingleOrDefaultAsync(u => u.Username == username);
            var msg = new Message
            {
                FromUserId = sender!.Id,
                Text = text,
                TimestampUtc = DateTime.UtcNow,
                FileUrl = fileUrl,
                FileName = fileName,
                FileType = fileType,
                FileSize = fileSize
            };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();

            await Clients.All.SendAsync("ReceiveMessage", username, text, msg.TimestampUtc, msg.Id, fileUrl, fileName, fileType, fileSize);
        }

        // Private message
        public async Task SendPrivateMessage(string toUsername, string text)
        {
            var fromUsername = CurrentUserName;
            var from = await _db.Users.SingleOrDefaultAsync(u => u.Username == fromUsername);
            var to = await _db.Users.SingleOrDefaultAsync(u => u.Username == toUsername);
            var msg = new Message
            {
                FromUserId = from!.Id,
                ToUserId = to?.Id,
                Text = text,
                TimestampUtc = DateTime.UtcNow,
                IsRead = false
            };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();

            var recipients = new List<string>();
            if (to != null && UserConnections.TryGetValue(to.Username, out var toConns))
                recipients.AddRange(toConns.Keys);
            if (UserConnections.TryGetValue(fromUsername, out var fromConns))
                recipients.AddRange(fromConns.Keys);

            if (recipients.Any())
                await Clients.Clients(recipients).SendAsync("ReceivePrivateMessage", fromUsername, toUsername, text, msg.TimestampUtc, msg.Id, null, null, null, null, msg.IsRead);
            else
                await Clients.Caller.SendAsync("ReceivePrivateMessage", fromUsername, toUsername, text, msg.TimestampUtc, msg.Id, null, null, null, null, msg.IsRead);
        }

        // Private file message
        public async Task SendPrivateFileMessage(string toUsername, string text, string fileUrl, string fileName, string fileType, long fileSize)
        {
            var fromUsername = CurrentUserName;
            var from = await _db.Users.SingleOrDefaultAsync(u => u.Username == fromUsername);
            var to = await _db.Users.SingleOrDefaultAsync(u => u.Username == toUsername);
            var msg = new Message
            {
                FromUserId = from!.Id,
                ToUserId = to?.Id,
                Text = text,
                TimestampUtc = DateTime.UtcNow,
                FileUrl = fileUrl,
                FileName = fileName,
                FileType = fileType,
                FileSize = fileSize,
                IsRead = false
            };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();

            var recipients = new List<string>();
            if (to != null && UserConnections.TryGetValue(to.Username, out var toConns))
                recipients.AddRange(toConns.Keys);
            if (UserConnections.TryGetValue(fromUsername, out var fromConns))
                recipients.AddRange(fromConns.Keys);

            if (recipients.Any())
                await Clients.Clients(recipients).SendAsync("ReceivePrivateMessage", fromUsername, toUsername, text, msg.TimestampUtc, msg.Id, fileUrl, fileName, fileType, fileSize, msg.IsRead);
            else
                await Clients.Caller.SendAsync("ReceivePrivateMessage", fromUsername, toUsername, text, msg.TimestampUtc, msg.Id, fileUrl, fileName, fileType, fileSize, msg.IsRead);
        }

        // Mark message as read
        public async Task MarkMessageAsRead(int msgId, string fromUsername)
        {
            var msg = await _db.Messages.FindAsync(msgId);
            if (msg != null && !msg.IsRead)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // Notify sender
                if (UserConnections.TryGetValue(fromUsername, out var conns))
                {
                    await Clients.Clients(conns.Keys.ToList()).SendAsync("MessageRead", msgId);
                }
            }
        }

        // Group join/leave
        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == groupName);
            if (group == null)
            {
                group = new Group { Name = groupName };
                _db.Groups.Add(group);
                await _db.SaveChangesAsync();
            }

            var user = await _db.Users.SingleAsync(u => u.Username == CurrentUserName);
            var exists = await _db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == group.Id);
            if (!exists)
            {
                _db.UserGroups.Add(new Models.UserGroup { UserId = user.Id, GroupId = group.Id });
                await _db.SaveChangesAsync();
            }

            await Clients.Group(groupName).SendAsync("GroupSystemMessage", $"{CurrentUserName} joined {groupName}", DateTime.UtcNow);
            await BroadcastGroups();
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == groupName);
            if (group != null)
            {
                var user = await _db.Users.SingleAsync(u => u.Username == CurrentUserName);
                var ug = await _db.UserGroups.SingleOrDefaultAsync(x => x.UserId == user.Id && x.GroupId == group.Id);
                if (ug != null) { _db.UserGroups.Remove(ug); await _db.SaveChangesAsync(); }
            }

            await Clients.Group(groupName).SendAsync("GroupSystemMessage", $"{CurrentUserName} left {groupName}", DateTime.UtcNow);
            await BroadcastGroups();
        }

        public async Task SendMessageToGroup(string groupName, string text)
        {
            var from = await _db.Users.SingleAsync(u => u.Username == CurrentUserName);
            var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == groupName);
            var msg = new Message
            {
                FromUserId = from.Id,
                GroupId = group?.Id,
                Text = text,
                TimestampUtc = DateTime.UtcNow
            };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();

            await Clients.Group(groupName).SendAsync("ReceiveGroupMessage", CurrentUserName, groupName, text, msg.TimestampUtc, msg.Id, null, null, null, null);
        }

        public async Task SendFileToGroup(string groupName, string text, string fileUrl, string fileName, string fileType, long fileSize)
        {
            var from = await _db.Users.SingleAsync(u => u.Username == CurrentUserName);
            var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == groupName);
            var msg = new Message
            {
                FromUserId = from.Id,
                GroupId = group?.Id,
                Text = text,
                TimestampUtc = DateTime.UtcNow,
                FileUrl = fileUrl,
                FileName = fileName,
                FileType = fileType,
                FileSize = fileSize
            };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync();

            await Clients.Group(groupName).SendAsync("ReceiveGroupMessage", CurrentUserName, groupName, text, msg.TimestampUtc, msg.Id, fileUrl, fileName, fileType, fileSize);
        }

        // Typing indicators
        public Task TypingToUser(string toUsername)
        {
            if (UserConnections.TryGetValue(toUsername, out var conns))
            {
                var ids = conns.Keys.ToList();
                return Clients.Clients(ids).SendAsync("UserTyping", CurrentUserName, toUsername);
            }
            return Task.CompletedTask;
        }

        public Task TypingToGroup(string groupName)
        {
            return Clients.Group(groupName).SendAsync("GroupTyping", CurrentUserName, groupName);
        }

        private Task BroadcastGroups()
        {
            return Task.CompletedTask;
        }
    }




    //[Authorize] // only authenticated users allowed
    //public class ChatHub : Hub
    //{
    //    private readonly AppDbContext _db;
    //    // in-memory tracking: username -> set of connectionIds
    //    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> UserConnections
    //        = new();

    //    public ChatHub(AppDbContext db) { _db = db; }

    //    private string CurrentUserName => Context.User?.Identity?.Name ?? "Unknown";

    //    public override async Task OnConnectedAsync()
    //    {
    //        var conn = Context.ConnectionId;
    //        var user = CurrentUserName;

    //        var conns = UserConnections.GetOrAdd(user, _ => new ConcurrentDictionary<string, byte>());
    //        conns[conn] = 0;

    //        // notify all clients of user list change
    //        await BroadcastUsers();
    //        await base.OnConnectedAsync();
    //    }

    //    public override async Task OnDisconnectedAsync(Exception? exception)
    //    {
    //        var conn = Context.ConnectionId;
    //        var user = CurrentUserName;

    //        if (UserConnections.TryGetValue(user, out var conns))
    //        {
    //            conns.TryRemove(conn, out _);
    //            if (conns.IsEmpty) UserConnections.TryRemove(user, out _);
    //        }

    //        await BroadcastUsers();
    //        await base.OnDisconnectedAsync(exception);
    //    }

    //    private Task BroadcastUsers()
    //    {
    //        var users = UserConnections.Keys.OrderBy(x => x).ToList();
    //        return Clients.All.SendAsync("UsersUpdated", users);
    //    }

    //    // Public broadcast
    //    public async Task SendMessageToAll(string text)
    //    {
    //        var username = CurrentUserName;
    //        var sender = await _db.Users.SingleOrDefaultAsync(u => u.Username == username);
    //        var msg = new Message { FromUserId = sender!.Id, Text = text, TimestampUtc = DateTime.UtcNow };
    //        _db.Messages.Add(msg);
    //        await _db.SaveChangesAsync();

    //        await Clients.All.SendAsync("ReceiveMessage", username, text, msg.TimestampUtc);
    //    }

    //    // private message
    //    public async Task SendPrivateMessage(string toUsername, string text)
    //    {
    //        var fromUsername = CurrentUserName;
    //        var from = await _db.Users.SingleOrDefaultAsync(u => u.Username == fromUsername);
    //        var to = await _db.Users.SingleOrDefaultAsync(u => u.Username == toUsername);
    //        var msg = new Message
    //        {
    //            FromUserId = from!.Id,
    //            ToUserId = to?.Id,
    //            Text = text,
    //            TimestampUtc = DateTime.UtcNow
    //        };
    //        _db.Messages.Add(msg);
    //        await _db.SaveChangesAsync();

    //        // send to recipient connections and sender
    //        var recipients = new List<string>();
    //        if (to != null && UserConnections.TryGetValue(to.Username, out var toConns))
    //            recipients.AddRange(toConns.Keys);
    //        // sender connections
    //        if (UserConnections.TryGetValue(fromUsername, out var fromConns))
    //            recipients.AddRange(fromConns.Keys);

    //        if (recipients.Any())
    //            await Clients.Clients(recipients).SendAsync("ReceivePrivateMessage", fromUsername, toUsername, text, msg.TimestampUtc);
    //        else
    //            await Clients.Caller.SendAsync("ReceivePrivateMessage", fromUsername, toUsername, text, msg.TimestampUtc);
    //    }

    //    // group join/leave
    //    public async Task JoinGroup(string groupName)
    //    {
    //        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    //        // ensure group in DB
    //        var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == groupName);
    //        if (group == null)
    //        {
    //            group = new Group { Name = groupName };
    //            _db.Groups.Add(group);
    //            await _db.SaveChangesAsync();
    //        }

    //        // add membership if not exists
    //        var user = await _db.Users.SingleAsync(u => u.Username == CurrentUserName);
    //        var exists = await _db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == group.Id);
    //        if (!exists)
    //        {
    //            _db.UserGroups.Add(new Models.UserGroup { UserId = user.Id, GroupId = group.Id });
    //            await _db.SaveChangesAsync();
    //        }

    //        await Clients.Group(groupName).SendAsync("GroupSystemMessage", $"{CurrentUserName} joined {groupName}", DateTime.UtcNow);
    //        await BroadcastGroups(); // optional
    //    }

    //    public async Task LeaveGroup(string groupName)
    //    {
    //        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    //        var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == groupName);
    //        if (group != null)
    //        {
    //            var user = await _db.Users.SingleAsync(u => u.Username == CurrentUserName);
    //            var ug = await _db.UserGroups.SingleOrDefaultAsync(x => x.UserId == user.Id && x.GroupId == group.Id);
    //            if (ug != null) { _db.UserGroups.Remove(ug); await _db.SaveChangesAsync(); }
    //        }

    //        await Clients.Group(groupName).SendAsync("GroupSystemMessage", $"{CurrentUserName} left {groupName}", DateTime.UtcNow);
    //        await BroadcastGroups();
    //    }

    //    public async Task SendMessageToGroup(string groupName, string text)
    //    {
    //        var from = await _db.Users.SingleAsync(u => u.Username == CurrentUserName);
    //        var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == groupName);
    //        var msg = new Message
    //        {
    //            FromUserId = from.Id,
    //            GroupId = group?.Id,
    //            Text = text,
    //            TimestampUtc = DateTime.UtcNow
    //        };
    //        _db.Messages.Add(msg);
    //        await _db.SaveChangesAsync();

    //        await Clients.Group(groupName).SendAsync("ReceiveGroupMessage", CurrentUserName, groupName, text, msg.TimestampUtc);
    //    }

    //    // typing
    //    public Task TypingToUser(string toUsername)
    //    {
    //        if (UserConnections.TryGetValue(toUsername, out var conns))
    //        {
    //            var ids = conns.Keys.ToList();
    //            return Clients.Clients(ids).SendAsync("UserTyping", CurrentUserName, toUsername);
    //        }
    //        return Task.CompletedTask;
    //    }

    //    public Task TypingToGroup(string groupName)
    //    {
    //        return Clients.Group(groupName).SendAsync("GroupTyping", CurrentUserName, groupName);
    //    }

    //    private Task BroadcastGroups()
    //    {
    //        // optionally broadcast groups for UI — keep simple now
    //        return Task.CompletedTask;
    //    }
    //}




}
