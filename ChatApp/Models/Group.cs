namespace ChatApp.Models
{
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public ICollection<UserGroup>? UserGroups { get; set; }
    }
}
