using System.Text.Json;

//: ======================================================================
//: Thread-safe, bounded in-memory diagnostics collected from the GenAI worker.
//: ======================================================================
public static class GenAiDiagnostics
{
    private static readonly object Sync = new();
    private static readonly List<JsonElement> Entries = new();

    //* Stores a diagnostic entry at the beginning of the bounded list.
    public static void Store(JsonElement entry)
    {
        lock (Sync)
        {
            Entries.Insert(0, entry);
            if (Entries.Count > 50) Entries.RemoveAt(Entries.Count - 1);
        }
    }

    public static IReadOnlyList<JsonElement> GetAll()
    {
        lock (Sync) return Entries.ToArray();
    }
}
