namespace ChatApp.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = null!;
        // plain text password per your request (NOT recommended for production)
        public string Password { get; set; } = null!;

        public ICollection<Message>? SentMessages { get; set; }
        public ICollection<UserGroup>? UserGroups { get; set; }
    }
}
