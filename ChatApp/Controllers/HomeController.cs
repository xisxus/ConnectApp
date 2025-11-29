using System.Diagnostics;
using ChatApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Controllers
{
    //[Authorize]
    //public class HomeController : Controller
    //{
    //    private readonly AppDbContext _db;
    //    public HomeController(AppDbContext db) { _db = db; }

    //    public IActionResult Chat() => View();

    //    // get online users (server-tracked)
    //    [HttpGet]
    //    public async Task<IActionResult> GetUsers()
    //    {
    //        // return all users - you might filter to online only if you track
    //        var users = await _db.Users.Select(u => new { u.Username }).OrderBy(u => u.Username).ToListAsync();
    //        return Json(users);
    //    }

    //    // get groups user joined
    //    [HttpGet]
    //    public async Task<IActionResult> GetMyGroups()
    //    {
    //        var username = User.Identity!.Name!;
    //        var user = await _db.Users.SingleAsync(u => u.Username == username);
    //        var groups = await _db.UserGroups
    //            .Where(ug => ug.UserId == user.Id)
    //            .Select(ug => new { ug.Group!.Name })
    //            .ToListAsync();
    //        return Json(groups);
    //    }

    //    // get conversation - type can be "public", "user", "group"
    //    [HttpGet]
    //    public async Task<IActionResult> GetConversation(string type, string? name)
    //    {
    //        var username = User.Identity!.Name!;
    //        var me = await _db.Users.SingleAsync(u => u.Username == username);

    //        if (type == "public")
    //        {
    //            var msgs = await _db.Messages
    //                .Where(m => m.ToUserId == null && m.GroupId == null)
    //                .OrderByDescending(m => m.TimestampUtc).Take(100)
    //                .Select(m => new { From = m.FromUser!.Username, Text = m.Text, m.TimestampUtc })
    //                .OrderBy(m => m.TimestampUtc).ToListAsync();
    //            return Json(msgs);
    //        }
    //        else if (type == "user" && name != null)
    //        {
    //            var other = await _db.Users.SingleOrDefaultAsync(u => u.Username == name);
    //            if (other == null) return Json(new object[0]);

    //            var msgs = await _db.Messages
    //                .Where(m => (m.FromUserId == me.Id && m.ToUserId == other.Id) || (m.FromUserId == other.Id && m.ToUserId == me.Id))
    //                .OrderBy(m => m.TimestampUtc)
    //                .Select(m => new { From = m.FromUser!.Username, To = m.ToUser!.Username, m.Text, m.TimestampUtc })
    //                .ToListAsync();
    //            return Json(msgs);
    //        }
    //        else if (type == "group" && name != null)
    //        {
    //            var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == name);
    //            if (group == null) return Json(new object[0]);

    //            var msgs = await _db.Messages
    //                .Where(m => m.GroupId == group.Id)
    //                .OrderBy(m => m.TimestampUtc)
    //                .Select(m => new { From = m.FromUser!.Username, m.Text, m.TimestampUtc })
    //                .ToListAsync();
    //            return Json(msgs);
    //        }
    //        return Json(new object[0]);
    //    }
    //}



    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public HomeController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public IActionResult Chat() => View();

        // get online users (server-tracked)
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
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
                    .Select(m => new
                    {
                        m.Id,
                        From = m.FromUser!.Username,
                        Text = m.Text,
                        m.TimestampUtc,
                        m.FileUrl,
                        m.FileName,
                        m.FileType,
                        m.FileSize,
                        IsRead = true
                    })
                    .OrderBy(m => m.TimestampUtc).ToListAsync();
                return Json(msgs);
            }
            else if (type == "user" && name != null)
            {
                var other = await _db.Users.SingleOrDefaultAsync(u => u.Username == name);
                if (other == null) return Json(new object[0]);

                var msgs = await _db.Messages
                    .Where(m => (m.FromUserId == me.Id && m.ToUserId == other.Id) || (m.FromUserId == other.Id && m.ToUserId == me.Id))
                    .OrderByDescending(m => m.TimestampUtc).Take(100)
                    .Select(m => new
                    {
                        m.Id,
                        From = m.FromUser!.Username,
                        To = m.ToUser!.Username,
                        m.Text,
                        m.TimestampUtc,
                        m.FileUrl,
                        m.FileName,
                        m.FileType,
                        m.FileSize,
                        m.IsRead
                    })
                    .OrderBy(m => m.TimestampUtc)
                    .ToListAsync();

                // Mark unread messages as read
                var unreadIds = msgs.Where(m => m.To == username && !m.IsRead).Select(m => m.Id).ToList();
                if (unreadIds.Any())
                {
                    var unreadMessages = await _db.Messages.Where(m => unreadIds.Contains(m.Id)).ToListAsync();
                    foreach (var msg in unreadMessages)
                    {
                        msg.IsRead = true;
                        msg.ReadAt = DateTime.UtcNow;
                    }
                    await _db.SaveChangesAsync();
                }

                return Json(msgs);
            }
            else if (type == "group" && name != null)
            {
                var group = await _db.Groups.SingleOrDefaultAsync(g => g.Name == name);
                if (group == null) return Json(new object[0]);

                var msgs = await _db.Messages
                    .Where(m => m.GroupId == group.Id)
                    .OrderByDescending(m => m.TimestampUtc).Take(100)
                    .Select(m => new
                    {
                        m.Id,
                        From = m.FromUser!.Username,
                        m.Text,
                        m.TimestampUtc,
                        m.FileUrl,
                        m.FileName,
                        m.FileType,
                        m.FileSize,
                        IsRead = true
                    })
                    .OrderBy(m => m.TimestampUtc)
                    .ToListAsync();
                return Json(msgs);
            }
            return Json(new object[0]);
        }

        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return Json(new { success = false, message = "No file uploaded" });

                // Validate file size (50MB max)
                if (file.Length > 52428800)
                    return Json(new { success = false, message = "File size exceeds 50MB limit" });

                // Validate file type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".mov", ".avi", ".pdf", ".doc", ".docx" };
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                    return Json(new { success = false, message = "File type not allowed" });

                // Create uploads directory if it doesn't exist
                var uploadsPath = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                // Generate unique filename
                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsPath, uniqueFileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Determine file type
                string fileType = "document";
                if (new[] { ".jpg", ".jpeg", ".png", ".gif" }.Contains(extension))
                    fileType = "image";
                else if (new[] { ".mp4", ".mov", ".avi" }.Contains(extension))
                    fileType = "video";

                return Json(new
                {
                    success = true,
                    fileUrl = $"/uploads/{uniqueFileName}",
                    fileName = file.FileName,
                    fileType = fileType,
                    fileSize = file.Length
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }






}
