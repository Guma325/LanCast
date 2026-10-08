using System.Collections.Concurrent;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using VideoStreaming.Audio;
using VideoStreaming.Video;

namespace VideoStreaming;

/// <summary>Mantém os espectadores (RTCPeerConnection) e repassa vídeo/áudio a todos.</summary>
public sealed record ViewerInfo(Guid Id, string Name, string Remote, DateTime Since, bool Connected);

public sealed class StreamHub
{
    private sealed class Viewer
    {
        public RTCPeerConnection Pc = null!;
        public string Remote = "";
        public volatile bool WaitingKeyframe = true;
        public int VideoPt = 96;
        public Guid Id;
        public string Name = "";
        public DateTime Since = DateTime.Now;
    }

    private readonly ConcurrentDictionary<Guid, Viewer> _viewers = new();
    

    public event Action? ViewersChanged;

    public IReadOnlyList<ViewerInfo> GetViewers() => _viewers.Values
        .OrderBy(v => v.Since)
        .Select(v => new ViewerInfo(v.Id, v.Name, v.Remote, v.Since, v.Pc.connectionState == RTCPeerConnectionState.connected))
        .ToList();

    public IReadOnlyList<string> ConnectedNames => GetViewers().Where(v => v.Connected).Select(v => v.Name).ToList();

    public void Kick(Guid id)
    {
        if (_viewers.TryRemove(id, out var v)) { try { v.Pc.Close("removido pelo host"); } catch { } ViewersChanged?.Invoke(); }
    }

    /// <summary>Expulsa todos os espectadores de um IP (usado ao banir).</summary>
    public void KickIp(string ip)
    {
        foreach (var kv in _viewers.Where(v => v.Value.Remote == ip).ToList()) Kick(kv.Key);
    }

    public void CloseAll()
    {
        foreach (var id in _viewers.Keys.ToList()) Kick(id);
    }

    public int ViewerCount => _viewers.Count(v => v.Value.Pc.connectionState == RTCPeerConnectionState.connected);

    public StreamHub(ScreenEncoder video, AudioMixer audio)
    {
        video.OnPacket += OnVideo;
        audio.OnOpusFrame += OnAudio;
    }

    public async Task<string> AcceptOfferAsync(string offerSdp, string remote, string name)
    {
        var pc = new RTCPeerConnection(new RTCConfiguration { X_ICEIncludeAllInterfaceAddresses = true });
        var id = Guid.NewGuid();
        var viewer = new Viewer { Pc = pc, Remote = remote, Id = id, Name = name };

        var vFmt = new VideoFormat(VideoCodecsEnum.H264, 96, 90000,
            "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f");
        var aFmt = new AudioFormat(AudioCodecsEnum.OPUS, 111, 48000, 2,
            "minptime=10;useinbandfec=1;stereo=1;sprop-stereo=1");
        pc.addTrack(new MediaStreamTrack(vFmt, MediaStreamStatusEnum.SendOnly));
        pc.addTrack(new MediaStreamTrack(aFmt, MediaStreamStatusEnum.SendOnly));

        pc.onconnectionstatechange += state =>
        {
            ViewersChanged?.Invoke();
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed or RTCPeerConnectionState.disconnected)
            {
                if (_viewers.TryRemove(id, out _)) pc.Close("encerrado");
            }
        };

        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offerSdp });
        if (result != SetDescriptionResultEnum.OK) throw new InvalidOperationException($"offer inválida: {result}");

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer);
        var (sdp, pt) = FixAnswer(pc.localDescription.sdp.ToString());
        viewer.VideoPt = pt;
        _viewers[id] = viewer;
        return sdp;
    }

    private void OnVideo(RtpVideoPacket p)
    {
        foreach (var v in _viewers.Values)
        {
            if (v.Pc.connectionState != RTCPeerConnectionState.connected) continue;
            if (v.WaitingKeyframe)
            {
                if (!p.StartsKeyframe) continue;               // novo espectador começa num IDR
                v.WaitingKeyframe = false;
            }
            try { v.Pc.SendRtpRaw(SDPMediaTypesEnum.video, p.Payload, p.Timestamp, p.Marker ? 1 : 0, v.VideoPt); }
            catch { }
        }
    }

    private void OnAudio(byte[] opus)
    {
        foreach (var v in _viewers.Values)
        {
            if (v.Pc.connectionState != RTCPeerConnectionState.connected) continue;
            try { v.Pc.SendAudio((uint)AudioMixer.FrameSamples, opus); }
            catch { }
        }
    }

    /// <summary>
    /// O SIPSorcery responde com todos os payload types H.264 do navegador e coloca os candidatos ICE só no último m-line.
    /// Aqui deixamos apenas o PT H.264 High (packetization-mode=1) e copiamos os candidatos para o m-line do BUNDLE.
    /// </summary>
    private static (string Sdp, int Pt) FixAnswer(string sdp)
    {
        var lines = sdp.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        int vStart = lines.FindIndex(l => l.StartsWith("m=video"));
        if (vStart < 0) return (sdp, 96);
        int vEnd = lines.FindIndex(vStart + 1, l => l.StartsWith("m=")); if (vEnd < 0) vEnd = lines.Count;

        // escolhe o payload type
        var cands = new List<(int pt, string fmtp)>();
        for (int i = vStart; i < vEnd; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"^a=fmtp:(\d+) (.*)$");
            if (m.Success && m.Groups[2].Value.Contains("packetization-mode=1"))
                cands.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value));
        }
        int pt = cands.Where(c => c.fmtp.Contains("profile-level-id=64")).Select(c => c.pt).DefaultIfEmpty(
                 cands.Select(c => c.pt).DefaultIfEmpty(96).First()).First();

        var video = new List<string>();
        for (int i = vStart; i < vEnd; i++)
        {
            var l = lines[i];
            if (i == vStart) { video.Add($"m=video 9 UDP/TLS/RTP/SAVP {pt}"); continue; }
            var m = System.Text.RegularExpressions.Regex.Match(l, @"^a=(?:rtpmap|fmtp|rtcp-fb):(\d+)");
            if (m.Success && int.Parse(m.Groups[1].Value) != pt) continue;
            video.Add(l);
        }
        // candidatos ICE de qualquer seção -> seção de vídeo (primeira do BUNDLE)
        var ice = lines.Where(l => l.StartsWith("a=candidate:") || l.StartsWith("a=end-of-candidates")).Distinct().ToList();
        video.RemoveAll(l => l.StartsWith("a=candidate:") || l.StartsWith("a=end-of-candidates"));
        int at = video.FindIndex(l => l.StartsWith("a=ice-ufrag"));
        video.InsertRange(at < 0 ? video.Count : at, ice);

        var outLines = lines.Take(vStart).Concat(video).Concat(lines.Skip(vEnd));
        return (string.Join("\r\n", outLines) + "\r\n", pt);
    }
}
