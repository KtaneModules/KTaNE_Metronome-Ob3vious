using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

public class MetronomeScript : MonoBehaviour
{
    bool TwitchPlaysActive;

    public Transform Dial;
    public Transform LabelLine;
    public TextMesh Label;
    public MeshRenderer Led;
    public Material[] LedMaterials;
    public KMSelectable[] Selectables;

    private KMNeedyModule _module;
    private bool _isAlive = false;

    private List<int> _bpms;

    private int _selectedBpm = 0;
    private int _dialValue = 0;
    private int _armState = 0;

    private KMAudio _audio;
    private KMAudio.KMAudioRef _audioRef = null;
    private bool _focused;

    private int _moduleId = 0;
    private static int _moduleIdCounter = 1;

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        _audio = GetComponent<KMAudio>();

        _bpms = CalculateBpms(40, 200, 39);
        GenerateLabels();

        _module = GetComponent<KMNeedyModule>();

        _module.OnNeedyActivation += () =>
        {
            _isAlive = true;
            _selectedBpm = _bpms.PickRandom();
            Log("The selected BPM is {0}.", _selectedBpm);
            StartCoroutine(MetronomeClick(_selectedBpm));
            StartCoroutine(TimerManipulate(_bpms.PickRandom()));
        };
        _module.OnTimerExpired += () =>
        {
            _isAlive = false;
            if (_dialValue != 0 && _bpms[_dialValue - 1] == _selectedBpm)
            {
                Log("The dial has been set to {0}. This is correct!", _bpms[_dialValue - 1]);
                if (_armState > 0)
                {
                    _armState--;
                    if (_armState >= 1)
                        Log("{0} required to disarm.", _armState + (_armState == 1 ? " more correct response" : " more correct responses"));
                    else
                        Log("Disarming the module.");
                }
            }
            else
            {
                Log("The dial has been set to {0}, when expected was {1}. This is incorrect!", _dialValue == 0 ? "A4" : _bpms[_dialValue - 1].ToString(), _selectedBpm);
                if (_armState == 0)
                {
                    Log("Arming the module.");
                    _armState += 2;
                    Log("{0} required to disarm.", _armState + (_armState == 1 ? " correct response" : " correct responses"));
                }
                else
                {
                    Log("The module was already armed. Strike!");
                    _module.HandleStrike();
                    _armState = 0;
                    Log("Disarming the module.");
                }
            }

            if (_audioRef != null)
            {
                _audioRef.StopSound();
                _audioRef = null;
            }
        };
        _module.OnNeedyDeactivation += () => _isAlive = false;

        for (int i = 0; i < 2; i++)
        {
            int i2 = i;
            Selectables[i].OnInteract += () =>
            {
                Selectables[i2].AddInteractionPunch(0.125f);
                GetComponent<KMAudio>().PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);

                if (i2 == 0)
                    _dialValue = (_dialValue + 39) % 40;
                else
                    _dialValue = (_dialValue + 1) % 40;

                Dial.localEulerAngles = new Vector3(-90, 360 * (_dialValue / 40f), 0);

                return false;
            };
        }

        KMSelectable moduleSelectable = GetComponent<KMSelectable>();
        moduleSelectable.OnFocus += () => { if (!TwitchPlaysActive) _focused = true; };
        moduleSelectable.OnDefocus += () => { if (!TwitchPlaysActive) _focused = false; };

        StartCoroutine(LedBlink());
    }

    private void OnDestroy()
    {
        _isAlive = false;

        if (_audioRef != null)
        {
            _audioRef.StopSound();
            _audioRef = null;
        }
    }

    private IEnumerator MetronomeClick(float bpm)
    {
        float time = 0;
        while (_isAlive)
        {
            while (time > 60f / bpm)
            {
                if (_focused)
                    _audio.PlaySoundAtTransform("click", transform);
                time -= 60f / bpm;
            }

            yield return null;
            time += Time.deltaTime;
        }
    }

    private IEnumerator LedBlink()
    {
        float time = 0;
        while (true)
        {
            int ledValue = 0;
            switch (_armState)
            {
                case 1:
                    ledValue = Mathf.RoundToInt(time % 1f);
                    break;
                case 2:
                    ledValue = Mathf.RoundToInt((time % (1 / 3f)) * 3);
                    break;
            }
            Led.material = LedMaterials[ledValue];
            yield return null;
            time += Time.deltaTime;
            time %= 1;
        }
    }

    private IEnumerator TimerManipulate(float bpm)
    {
        float time = 60;
        while (_isAlive)
        {
            float visualTime = time * bpm / 60;

            _module.SetNeedyTimeRemaining(visualTime);

            yield return null;
            time -= Time.deltaTime;
            if (time < 0)
                time = Mathf.Epsilon;
            if (time <= 5 && _audioRef == null)
                _audioRef = _audio.PlayGameSoundAtTransformWithRef(KMSoundOverride.SoundEffect.NeedyWarning, transform);
        }

        if (_audioRef != null)
        {
            _audioRef.StopSound();
            _audioRef = null;
        }
    }

    private void GenerateLabels()
    {
        for (int i = 0; i < _bpms.Count; i++)
        {
            float angle = (i + 1f) / (_bpms.Count + 1f);
            TextMesh newText = Instantiate(Label, Label.transform.parent);
            newText.text = _bpms[i].ToString();
            newText.transform.localPosition = new Vector3(
                Mathf.Cos(angle * Mathf.PI * 2) * Label.transform.localPosition.x + Mathf.Sin(angle * Mathf.PI * 2) * Label.transform.localPosition.z,
                Label.transform.localPosition.y,
                Mathf.Cos(angle * Mathf.PI * 2) * Label.transform.localPosition.z - Mathf.Sin(angle * Mathf.PI * 2) * Label.transform.localPosition.x);
            Transform newLine = Instantiate(LabelLine, LabelLine.parent);
            newLine.transform.localEulerAngles += new Vector3(0, angle * 360, 0);
        }
    }

    private List<int> CalculateBpms(int min, int max, int count)
    {
        //return Enumerable.Range(0, count).Select(x => Mathf.RoundToInt(1f / (x / (count - 1f) * (1f / min - 1f / max) + 1f / max))).ToList();
        List<int> bpms = Enumerable.Range(0, count).Select(x => Mathf.RoundToInt(Mathf.Pow(10, x / (count - 1f) * Mathf.Log10((float)max / min) + Mathf.Log10(min)))).ToList();
        List<int> bpmDifferences = new List<int>();
        for (int i = 0; i < bpms.Count - 1; i++)
            bpmDifferences.Add(bpms[i + 1] - bpms[i]);

        for (int i = 0; i < bpmDifferences.Count - 1; i++)
            while (bpmDifferences[i + 1] < bpmDifferences[i])
            {
                bpmDifferences[i + 1]++;
                if (i < bpmDifferences.Count - 2)
                    bpmDifferences[i + 2]--;
                bpms[i + 2]++;
            }

        return bpms;
    }

    private void Log(string text, params object[] args)
    {
        Debug.LogFormat("[Metronome #{0}] {1}", _moduleId, string.Format(text, args));
    }

#pragma warning disable 414
    private string TwitchHelpMessage = "'!{0} mute/unmute' to turn off and on the sound. '!{0} set 68' to set the dial to 68 BPM.";
#pragma warning restore 414
    IEnumerator ProcessTwitchCommand(string command)
    {
        yield return null;

        command = command.ToLowerInvariant();
        string[] commands = command.Split(' ');
        if (commands.Length == 1 && command == "mute" || command == "unmute")
        {
            _focused = command == "unmute";
        }
        else if (commands.Length == 2 && commands[0] == "set" && commands[1].RegexMatch(@"^(\d{2,3}|a4)$"))
        {
            int index;
            if (commands[1] == "a4")
                index = 0;
            else
            {
                int value;
                if (!int.TryParse(commands[1], out value) || !_bpms.Contains(value))
                {
                    yield return "sendtochaterror Invalid command.";
                    yield break;
                }
                index = _bpms.IndexOf(value) + 1;
            }

            int stepsForward = (_dialValue - index + 40) % 40;
            if (stepsForward >= 20)
                stepsForward = stepsForward - 40;

            while (stepsForward != 0)
            {
                Selectables[stepsForward > 0 ? 0 : 1].OnInteract();
                stepsForward -= (int)Mathf.Sign(stepsForward);
                yield return new WaitForSeconds(0.05f);
            }
        }
        else
        {
            yield return "sendtochaterror Invalid command.";
            yield break;
        }
    }

    IEnumerator TwitchHandleForcedSolve()
    {
        yield return null;
        while (true)
        {
            int stepsForward = (_dialValue - (_bpms.IndexOf(_selectedBpm) + 1) + 40) % 40;
            if (stepsForward >= 20)
                stepsForward = stepsForward - 40;
            while (stepsForward != 0 && _isAlive)
            {
                Selectables[stepsForward > 0 ? 0 : 1].OnInteract();
                stepsForward -= (int)Mathf.Sign(stepsForward);
                yield return new WaitForSeconds(0.05f);
            }
            yield return true;
        }
    }
}
