using System.Text;

namespace StreamClipper.Services;


public class EmbeddingFileHandler
{
    private const string MAGIC = "EMBD";
    private const uint VERSION = 1;

    public struct EmbeddingFileHeader
    {
        public string Magic;
        public uint Version;
        public uint Dimensions;
        public uint RecordCount;
    }

    public static async Task WriteEmbeddingsAsync(string filePath, float[][] embeddings, int dimensions)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // Write header
        writer.Write(Encoding.ASCII.GetBytes(MAGIC));
        writer.Write(VERSION);
        writer.Write((uint)dimensions);
        writer.Write((uint)embeddings.Length);

        // Write embeddings
        foreach (var embedding in embeddings)
        {
            foreach (var value in embedding)
            {
                writer.Write(value);
            }
        }

        await stream.FlushAsync();
    }

    public static async Task<(EmbeddingFileHeader header, float[][] embeddings)> ReadEmbeddingsAsync(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);

        // Read header
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != MAGIC)
        {
            throw new InvalidDataException($"Invalid file format. Expected magic '{MAGIC}', got '{magic}'");
        }

        var header = new EmbeddingFileHeader
        {
            Magic = magic,
            Version = reader.ReadUInt32(),
            Dimensions = reader.ReadUInt32(),
            RecordCount = reader.ReadUInt32()
        };

        if (header.Version != VERSION)
        {
            throw new InvalidDataException($"Unsupported version {header.Version}. Expected {VERSION}");
        }

        // Read embeddings
        var embeddings = new float[header.RecordCount][];
        for (int i = 0; i < header.RecordCount; i++)
        {
            embeddings[i] = new float[header.Dimensions];
            for (int j = 0; j < header.Dimensions; j++)
            {
                embeddings[i][j] = reader.ReadSingle();
            }
        }

        return await Task.FromResult((header, embeddings));
    }

    public static bool EmbeddingFileExists(string jsonlPath)
    {
        var binPath = Path.ChangeExtension(jsonlPath, ".bin");
        return File.Exists(binPath);
    }

    public static string GetEmbeddingFilePath(string jsonlPath)
    {
        return Path.ChangeExtension(jsonlPath, ".bin");
    }
}