using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.WebRTC;
using Unity.WebRTC.Samples;
using UnityEngine;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;
using Toggle = UnityEngine.UI.Toggle;

class BandwidthSample : MonoBehaviour
{
#pragma warning disable 0649
    [SerializeField] private Dropdown bandwidthSelector;
    [SerializeField] private Dropdown scaleResolutionDownSelector;
    [SerializeField] private Dropdown framerateSelector;
    [SerializeField] private Button callButton;
    [SerializeField] private Button hangUpButton;
    [SerializeField] private InputField statsField;
    [SerializeField] private Toggle autoScroll;
    [SerializeField] private Button copyClipboard;

    [SerializeField] private Camera cam;
    [SerializeField] private RawImage sourceImage;
    [SerializeField] private RawImage receiveImage;
    [SerializeField] private Transform rotateObject;
#pragma warning restore 0649

    private RTCPeerConnection _pc1, _pc2;
    private List<RTCRtpSender> pc1Senders;
    private MediaStream videoStream, receiveStream;
    private DelegateOnIceConnectionChange pc1OnIceConnectionChange;
    private DelegateOnIceConnectionChange pc2OnIceConnectionChange;
    private DelegateOnIceCandidate pc1OnIceCandidate;
    private DelegateOnIceCandidate pc2OnIceCandidate;
    private DelegateOnTrack pc2Ontrack;
    private DelegateOnNegotiationNeeded pc1OnNegotiationNeeded;
    private bool videoUpdateStarted;

    private Dictionary<string, ulong?> bandwidthOptions =
        new Dictionary<string, ulong?>()
    {
        { "undefined", null },
        { "10000", 10000 },
        { "2000", 2000 },
        { "1000", 1000 },
        { "500",  500 },
        { "250",  250 },
        { "125",  125 },
    };

    private Dictionary<string, double> scaleResolutionDownOptions =
        new Dictionary<string, double>()
    {
        { "Not scaling", 1.0f },
        { "Down scale by 2.0", 2.0f },
        { "Down scale by 4.0", 4.0f },
        { "Down scale by 8.0", 8.0f },
        { "Down scale by 16.0", 16.0f }
    };

    private Dictionary<string, uint?> framerateOptions =
        new Dictionary<string, uint?>
    {
        { "undefined", null },
        { "90", 90 },
        { "60", 60 },
        { "30", 30 },
        { "20", 20 },
        { "10", 10 },
        { "5", 5 },
        { "0", 0 },
    };

    private void Awake()
    {
        bandwidthSelector.options = bandwidthOptions
            .Select(pair => new Dropdown.OptionData { text = pair.Key })
            .ToList();
        bandwidthSelector.onValueChanged.AddListener(ChangeBandwitdh);
        scaleResolutionDownSelector.options = scaleResolutionDownOptions
            .Select(pair => new Dropdown.OptionData { text = pair.Key })
            .ToList();
        scaleResolutionDownSelector.onValueChanged.AddListener(ChangeScaleResolutionDown);

        framerateSelector.options = framerateOptions
            .Select(pair => new Dropdown.OptionData { text = pair.Key })
            .ToList();
        framerateSelector.onValueChanged.AddListener(ChangeFramerate);

        callButton.onClick.AddListener(Call);
        hangUpButton.onClick.AddListener(HangUp);
        copyClipboard.onClick.AddListener(CopyClipboard);
        receiveStream = new MediaStream();
    }

    private void Start()
    {
        pc1Senders = new List<RTCRtpSender>();
        callButton.interactable = true;
        hangUpButton.interactable = false;
        bandwidthSelector.interactable = false;
        scaleResolutionDownSelector.interactable = false;

        pc1OnIceConnectionChange = state => { OnIceConnectionChange(_pc1, state); };
        pc2OnIceConnectionChange = state => { OnIceConnectionChange(_pc2, state); };
        pc1OnIceCandidate = candidate => { OnIceCandidate(_pc1, candidate); };
        pc2OnIceCandidate = candidate => { OnIceCandidate(_pc2, candidate); };
        pc2Ontrack = e =>
        {
            receiveStream.AddTrack(e.Track);
        };
        pc1OnNegotiationNeeded = () => { StartCoroutine(PeerNegotiationNeeded(_pc1)); };

        receiveStream.OnAddTrack = e =>
        {
            if (e.Track is VideoStreamTrack track)
            {
                track.OnVideoReceived += OnVideoReceived;
            }
        };
    }

    private void OnVideoReceived(Texture tex)
    {
        receiveImage.texture = tex;
        receiveImage.color = Color.white;

        statsField.text +=
            $"Video resolution: {tex.width}x{tex.height}" + Environment.NewLine;
        if (autoScroll.isOn)
            statsField.MoveTextEnd(false);
    }

    private void Update()
    {
        if (rotateObject != null)
        {
            float t = Time.deltaTime;
            rotateObject.Rotate(100 * t, 200 * t, 300 * t);
        }
    }

    private static RTCConfiguration GetSelectedSdpSemantics()
    {
        RTCConfiguration config = default;
        config.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };

        return config;
    }

    private void OnIceConnectionChange(RTCPeerConnection pc, RTCIceConnectionState state)
    {
        switch (state)
        {
            case RTCIceConnectionState.New:
                Debug.Log($"{GetName(pc)} IceConnectionState: New");
                break;
            case RTCIceConnectionState.Checking:
                Debug.Log($"{GetName(pc)} IceConnectionState: Checking");
                break;
            case RTCIceConnectionState.Closed:
                Debug.Log($"{GetName(pc)} IceConnectionState: Closed");
                break;
            case RTCIceConnectionState.Completed:
                Debug.Log($"{GetName(pc)} IceConnectionState: Completed");
                break;
            case RTCIceConnectionState.Connected:
                Debug.Log($"{GetName(pc)} IceConnectionState: Connected");
                break;
            case RTCIceConnectionState.Disconnected:
                Debug.Log($"{GetName(pc)} IceConnectionState: Disconnected");
                break;
            case RTCIceConnectionState.Failed:
                Debug.Log($"{GetName(pc)} IceConnectionState: Failed");
                break;
            case RTCIceConnectionState.Max:
                Debug.Log($"{GetName(pc)} IceConnectionState: Max");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    IEnumerator PeerNegotiationNeeded(RTCPeerConnection pc)
    {
        var op = pc.CreateOffer();
        yield return op;

        if (!op.IsError)
        {
            if (pc.SignalingState != RTCSignalingState.Stable)
            {
                Debug.LogError($"{GetName(pc)} signaling state is not stable.");
                yield break;
            }

            yield return StartCoroutine(OnCreateOfferSuccess(pc, op.Desc));
        }
        else
        {
            OnCreateSessionDescriptionError(op.Error);
        }
    }

    private void AddTracks()
    {
        foreach (var track in videoStream.GetTracks())
        {
            pc1Senders.Add(_pc1.AddTrack(track, videoStream));
        }

        if (WebRTCSettings.UseVideoCodec != null)
        {
            var codecs = new[] { WebRTCSettings.UseVideoCodec };
            foreach (var transceiver in _pc1.GetTransceivers())
            {
                if (pc1Senders.Contains(transceiver.Sender))
                {
                    transceiver.SetCodecPreferences(codecs);
                }
            }
        }

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            StartCoroutine(LoopStatsCoroutine());
            videoUpdateStarted = true;
        }

        bandwidthSelector.interactable = false;
        scaleResolutionDownSelector.interactable = false;
    }

    private void RemoveTracks()
    {
        foreach (var sender in pc1Senders)
        {
            _pc1.RemoveTrack(sender);
        }
        pc1Senders.Clear();

        MediaStreamTrack[] tracks = receiveStream.GetTracks().ToArray();
        foreach (var track in tracks)
        {
            receiveStream.RemoveTrack(track);
            track.Dispose();
        }
    }

    private void Call()
    {
        callButton.interactable = false;
        hangUpButton.interactable = true;
        bandwidthSelector.interactable = true;
        scaleResolutionDownSelector.interactable = true;
        statsField.text = string.Empty;

        var configuration = GetSelectedSdpSemantics();
        _pc1 = new RTCPeerConnection(ref configuration);

        _pc1.OnIceCandidate = pc1OnIceCandidate;
        _pc1.OnIceConnectionChange = pc1OnIceConnectionChange;
        _pc1.OnNegotiationNeeded = pc1OnNegotiationNeeded;
        _pc2 = new RTCPeerConnection(ref configuration);

        _pc2.OnIceCandidate = pc2OnIceCandidate;
        _pc2.OnIceConnectionChange = pc2OnIceConnectionChange;
        _pc2.OnTrack = pc2Ontrack;

        if (videoStream == null)
        {
            videoStream = cam.CaptureStream(WebRTCSettings.StreamSize.x, WebRTCSettings.StreamSize.y);
        }
        sourceImage.texture = cam.targetTexture;
        sourceImage.color = Color.white;

        AddTracks();
    }

    private void ChangeBandwitdh(int index)
    {
        if (_pc1 == null || _pc2 == null)
            return;
        ulong? bandwidth = bandwidthOptions.Values.ElementAt(index);
        RTCRtpSender sender = _pc1.GetSenders().First();
        RTCRtpSendParameters parameters = sender.GetParameters();
        if (bandwidth == null)
        {
            parameters.encodings[0].maxBitrate = null;
            parameters.encodings[0].minBitrate = null;
        }
        else
        {
            parameters.encodings[0].maxBitrate = bandwidth * 1000;
            parameters.encodings[0].minBitrate = bandwidth * 1000;
        }

        RTCError error = sender.SetParameters(parameters);
        if (error.errorType != RTCErrorType.None)
        {
            Debug.LogErrorFormat("RTCRtpSender.SetParameters failed {0}", error.errorType);
            statsField.text += $"Failed change bandwidth to {bandwidth * 1000}{Environment.NewLine}";
            bandwidthSelector.value = 0;
        }
    }

    private void ChangeScaleResolutionDown(int index)
    {
        if (_pc1 == null || _pc2 == null)
            return;
        double scale = scaleResolutionDownOptions.Values.ElementAt(index);
        RTCRtpSender sender = _pc1.GetSenders().First();
        RTCRtpSendParameters parameters = sender.GetParameters();
        parameters.encodings[0].scaleResolutionDownBy = scale;

        RTCError error = sender.SetParameters(parameters);
        if (error.errorType != RTCErrorType.None)
        {
            Debug.LogErrorFormat("RTCRtpSender.SetParameters failed {0}", error.errorType);
            statsField.text +=
                $"Failed scale down video resolution to " +
                $"{(int)(WebRTCSettings.StreamSize.x / scale)}x{(int)(WebRTCSettings.StreamSize.y / scale)}{Environment.NewLine}";
            scaleResolutionDownSelector.value = 0;
        }
    }

    private void ChangeFramerate(int index)
    {
        if (_pc1 == null || _pc2 == null)
            return;
        uint? framerate = framerateOptions.Values.ElementAt(index);
        RTCRtpSender sender = _pc1.GetSenders().First();
        RTCRtpSendParameters parameters = sender.GetParameters();
        parameters.encodings[0].maxFramerate = framerate;
        RTCError error = sender.SetParameters(parameters);
        if (error.errorType != RTCErrorType.None)
        {
            Debug.LogErrorFormat("RTCRtpSender.SetParameters failed {0}", error.errorType);
            statsField.text +=
                $"Failed maxFramerate to " +
                $"{framerate}{Environment.NewLine}";
            framerateSelector.value = 0;
        }
    }

    private void HangUp()
    {
        RemoveTracks();

        _pc1.Close();
        _pc2.Close();
        Debug.Log("Close local/remote peer connection");
        _pc1.Dispose();
        _pc2.Dispose();
        _pc1 = null;
        _pc2 = null;
        callButton.interactable = true;
        hangUpButton.interactable = false;
        bandwidthSelector.interactable = false;
        bandwidthSelector.value = 0;
        scaleResolutionDownSelector.interactable = false;
        scaleResolutionDownSelector.value = 0;

        sourceImage.color = Color.black;
        receiveImage.color = Color.black;
    }

    private void CopyClipboard()
    {
#if UNITY_EDITOR
        UnityEditor.EditorGUIUtility.systemCopyBuffer = statsField.text;
#endif
    }

    private void OnIceCandidate(RTCPeerConnection pc, RTCIceCandidate candidate)
    {
        GetOtherPc(pc).AddIceCandidate(candidate);
        Debug.Log($"{GetName(pc)} ICE candidate:\n {candidate.Candidate}");
    }

    private string GetName(RTCPeerConnection pc)
    {
        return (pc == _pc1) ? "pc1" : "pc2";
    }

    private RTCPeerConnection GetOtherPc(RTCPeerConnection pc)
    {
        return (pc == _pc1) ? _pc2 : _pc1;
    }

    private IEnumerator LoopStatsCoroutine()
    {
        while (true)
        {
            yield return StartCoroutine(UpdateStatsCoroutine());
            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator UpdateStatsCoroutine()
    {
        RTCRtpSender sender = pc1Senders.FirstOrDefault();
        if (sender == null)
            yield break;
        RTCStatsReportAsyncOperation op = sender.GetStats();
        yield return op;
        if (op.IsError)
        {
            Debug.LogErrorFormat("RTCRtpSender.GetStats() is failed {0}", op.Error.errorType);
        }
        else
        {
            UpdateStatsPacketSize(op.Value);
        }
    }

    private RTCStatsReport lastResult = null;
    private void UpdateStatsPacketSize(RTCStatsReport res)
    {
        foreach (RTCStats stats in res.Stats.Values)
        {
            if (!(stats is RTCOutboundRTPStreamStats report))
            {
                continue;
            }

            long now = report.Timestamp;
            ulong bytes = report.bytesSent;

            if (lastResult != null)
            {
                if (!lastResult.TryGetValue(report.Id, out RTCStats last))
                    continue;

                var lastStats = last as RTCOutboundRTPStreamStats;
                var duration = (double)(now - lastStats.Timestamp) / 1000000;
                ulong bitrate = (ulong)(8 * (bytes - lastStats.bytesSent) / duration);
                statsField.text += $"Bitrate: {bitrate}" + Environment.NewLine;
                if (autoScroll.isOn)
                    statsField.MoveTextEnd(false);
            }

        }
        lastResult = res;
    }

    private IEnumerator OnCreateOfferSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Offer from {GetName(pc)}\n{desc.sdp}");
        Debug.Log($"{GetName(pc)} setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        var otherPc = GetOtherPc(pc);
        Debug.Log($"{GetName(otherPc)} setRemoteDescription start");
        var op2 = otherPc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
        }
        Debug.Log($"{GetName(otherPc)} createAnswer start");
        // Since the 'remote' side has no media stream we need
        // to pass in the right constraints in order for it to
        // accept the incoming offer of audio and video.

        var op3 = otherPc.CreateAnswer();
        yield return op3;
        if (!op3.IsError)
        {
            yield return OnCreateAnswerSuccess(otherPc, op3.Desc);
        }
        else
        {
            OnCreateSessionDescriptionError(op3.Error);
        }
    }

    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"{GetName(pc)} SetLocalDescription complete");
    }

    static void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }

    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"{GetName(pc)} SetRemoteDescription complete");

        if (pc == _pc1)
        {
            bandwidthSelector.interactable = true;
            scaleResolutionDownSelector.interactable = true;
        }
    }

    IEnumerator OnCreateAnswerSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Answer from {GetName(pc)}:\n{desc.sdp}");
        Debug.Log($"{GetName(pc)} setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        var otherPc = GetOtherPc(pc);
        Debug.Log($"{GetName(otherPc)} setRemoteDescription start");

        var op2 = otherPc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
        }
    }

    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }
}
