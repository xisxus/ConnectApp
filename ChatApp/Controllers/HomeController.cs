using System.Diagnostics;
using ChatApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        public HomeController(AppDbContext db) { _db = db; }

        public IActionResult Chat() => View();

        // get online users (server-tracked)
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            // return all users - you might filter to online only if you track
            var users = await _db.Users.Select(u => new { u.Username }).OrderBy(u => u.Username).ToListAsync();
            return Json(users);
        }

        // get groups user joined
        [HttpGet]
        public async Task<IActionResult> GetMyGroups()
        {
            var username = User.Identity!.Name!;
            var user = await _db.Users.SingleAsync(u => u.Username == username);
            var groups = await _db.UserGroups
                .Where(ug => ug.UserId == user.Id)
                .Select(ug => new { ug.Group!.Name })
                .ToListAsync();
            return Json(groups);
        }

        // get conversation - type can be "public", "user", "group"
        [HttpGet]
        public async Task<IActionResult> GetConversation(string type, string? name)
        {
            var username = User.Identity!.Name!;
            var me = await _db.Users.SingleAsync(u => u.Username == username);

            if (type == "public")
            {
                var msgs = await _db.Messages
                    .Where(m => m.ToUserId == null && m.GroupId == null)
                    .OrderByDescending(m => m.TimestampUtc).Take(100)
                    .Select(m => new { From = m.FromUser!.Username, Text = m.Text, m.TimestampUtc })
                    .OrderBy(m => m.TimestampUtc).ToListAsync();
                return Json(msgs);
            }
            else if (type == "user" && name != null)
            {
                var other = await _db.Users.SingleOrDefaultAsync(u => u.Username == name);
                if (other == null) return Json(new object[0]);

                var msgs = await _db.Messages
                    .Where(m => (m.FromUserId == me.Id && m.ToUserId == other.Id) || (m.FromUserId == other.Id && m.ToUserId == me.Id))
                    .OrderBy(m => m.TimestampUtc)
                    .Select(m => new { From = m.FromUser!.Username, To = m.ToUser!.Username, m.Text, m.TimestampUtc })
                    .ToListAsync();
                return Json(msgs);
            }
            else if (type == "group" && name != null)
            {
                var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == name);
                if (group == null) return Json(new object[0]);

                var msgs = await _db.Messages
                    .Where(m => m.GroupId == group.Id)
                    .OrderBy(m => m.TimestampUtc)
                    .Select(m => new { From = m.FromUser!.Username, m.Text, m.TimestampUtc })
                    .ToListAsync();
                return Json(msgs);
            }
            return Json(new object[0]);
        }
    }
}
