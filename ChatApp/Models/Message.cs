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

        public string Text { get; set; } = null!;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}