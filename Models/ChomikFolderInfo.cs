namespace Chomyfin.Models
{
    public class ChomikFolderInfo
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }

        public ChomikFolderInfo() { }

        public ChomikFolderInfo(long id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path;
        }

        public override string ToString() => $"[Folder {Id}] {Path}";
    }
}
