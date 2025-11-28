namespace ChatApp.Models
{

    public class Message
    {
        public int Id { get; set; }

        // who sent
        public int FromUserId { get; set; }
        public User? FromUser { get; set; }

        // if private message -> ToUserId set, for group -> GroupId set
        public int? ToUserId { get; set; }
        public User? ToUser { get; set; }

        public int? GroupId { get; set; }
        public Group? Group { get; set; }

        public string? Text { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        // File attachment properties
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
        public string? FileType { get; set; } // "image", "video", "document"
        public long? FileSize { get; set; }

        // Read status
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }
    }


    //public class Message
    //{
    //    public int Id { get; set; }

    //    // who sent
    //    public int FromUserId { get; set; }
    //    public User? FromUser { get; set; }

    //    // if private message -> ToUserId set, for group -> GroupId set
    //    public int? ToUserId { get; set; }
    //    public User? ToUser { get; set; }

    //    public int? GroupId { get; set; }
    //    public Group? Group { get; set; }

    //    public string Text { get; set; } = null!;
    //    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    //}
}