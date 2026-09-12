namespace PixelArtDownscale;

internal static class ParallelImageHelper
{
    public static int NormalizeThreadCount(int threadCount, int itemCount)
    {
        threadCount = Math.Max(1, threadCount);
        return Math.Min(threadCount, Math.Max(1, itemCount));
    }

    public static IEnumerable<(int Start, int End)> GetRowChunks(int height, int threadCount)
    {
        threadCount = NormalizeThreadCount(threadCount, height);
        int chunkSize = (height + threadCount - 1) / threadCount;

        for (int i = 0; i < threadCount; i++)
        {
            int start = i * chunkSize;
            if (start >= height)
                yield break;

            yield return (start, Math.Min(start + chunkSize, height));
        }
    }

    public static ParallelOptions CreateOptions(int threadCount)
        => new() { MaxDegreeOfParallelism = Math.Max(1, threadCount) };

    public static void ForEachRowChunk(int height, int threadCount, Action<int, int> processChunk)
    {
        threadCount = NormalizeThreadCount(threadCount, height);

        if (threadCount == 1)
        {
            // Never run on the caller thread (e.g. UI) — always use the thread pool.
            Task.Run(() => processChunk(0, height)).GetAwaiter().GetResult();
            return;
        }

        Parallel.ForEach(
            GetRowChunks(height, threadCount).ToList(),
            CreateOptions(threadCount),
            chunk => processChunk(chunk.Start, chunk.End));
    }
}
