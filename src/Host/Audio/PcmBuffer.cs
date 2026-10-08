namespace VideoStreaming.Audio;

/// <summary>Buffer circular de PCM 16-bit com latência limitada (descarta o mais antigo).</summary>
public sealed class PcmBuffer
{
    private readonly short[] _ring = new short[ProcessLoopbackCapture.SampleRate * ProcessLoopbackCapture.Channels / 4]; // 250 ms
    private int _read, _count;
    private readonly object _lock = new();

    public void Write(short[] src, int count)
    {
        lock (_lock)
        {
            if (count > _ring.Length) { src = src[(count - _ring.Length)..count]; count = _ring.Length; }
            int overflow = _count + count - _ring.Length;
            if (overflow > 0) { _read = (_read + overflow) % _ring.Length; _count -= overflow; }
            int w = (_read + _count) % _ring.Length;
            int first = Math.Min(count, _ring.Length - w);
            Array.Copy(src, 0, _ring, w, first);
            if (first < count) Array.Copy(src, first, _ring, 0, count - first);
            _count += count;
        }
    }

    /// <summary>Soma 'count' amostras no acumulador. Falta de dados vira silêncio.</summary>
    public void MixInto(int[] acc, int count, int maxBacklog)
    {
        lock (_lock)
        {
            // mantém a latência baixa: se acumulou demais, pula o excedente
            if (_count > maxBacklog)
            {
                int drop = _count - maxBacklog;
                _read = (_read + drop) % _ring.Length;
                _count -= drop;
            }
            int n = Math.Min(count, _count);
            for (int i = 0; i < n; i++)
                acc[i] += _ring[(_read + i) % _ring.Length];
            _read = (_read + n) % _ring.Length;
            _count -= n;
        }
    }
}
