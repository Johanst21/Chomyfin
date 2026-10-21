namespace Chomyfin.Models
{
    public class ChomikFileInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }

        public ChomikFileInfo() { }

        public ChomikFileInfo(string id, string name, string path, long size = 0)
        {
            Id = id;
            Name = name;
            Path = path;
            Size = size;
        }

        public override string ToString() => $"[File {Id}] {Path} ({Size} bytes)";
    }
}
