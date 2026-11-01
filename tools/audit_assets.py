"""Verify upstream/native preservation and the compiled embedded cue using local inputs."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import wave
from ecma import Assembly


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ['source', 'baseline', 'previous-native', 'client-dll', 'references', 'output']:
        parser.add_argument('--' + key, required=True, type=Path)
    args = parser.parse_args()
    source, baseline, previous = args.source, args.baseline, args.previous_native
    args.output.mkdir(parents=True, exist_ok=True)
    def record(checks, name, ok):
        checks.append({'name': name, 'status': 'PASS' if ok else 'FAIL'})
    def report(name, checks, **extra):
        result = {'passed': sum(c['status']=='PASS' for c in checks), 'failed': sum(c['status']=='FAIL' for c in checks), **extra, 'checks': checks}
        (args.output / (name + '.json')).write_text(json.dumps(result, indent=2) + '\n')
        print(name, result['passed'], 'passed,', result['failed'], 'failed')
        return result['failed']
    checks = []
    for rel in ['src/EscapeFromBorderlands.Client/LegendaryDefinitions.json', 'src/EscapeFromBorderlands.Server/LegendaryDefinitions.json', 'config/config.json', 'src/EscapeFromBorderlands.Client/RarityResolver.cs', 'src/EscapeFromBorderlands.Client/EarpieceEffects.cs', 'src/EscapeFromBorderlands.Client/EarpieceEffectRules.cs', 'src/EscapeFromBorderlands.Client/WavePcm.cs', 'src/EscapeFromBorderlands.Client/Audio/HolyHandGrenade.wav', 'src/EscapeFromBorderlands.Client/Audio/README.md', 'src/EscapeFromBorderlands.Client/ElephantGunRules.cs', 'src/EscapeFromBorderlands.Client/BackpackStorageVariants.cs', 'src/EscapeFromBorderlands.Server/BackpackStorageVariants.cs']:
        record(checks, 'Current upstream bytes preserved: '+rel, (source/rel).read_bytes()==(baseline/rel).read_bytes())
    for rel in ['src/EscapeFromBorderlands.Client/BackpackPresentation.cs', 'src/EscapeFromBorderlands.Client/LegendaryQuestItems.cs', 'src/EscapeFromBorderlands.Server/LegendaryQuestItems.cs', 'src/EscapeFromBorderlands.Shared/QuestItemEquivalence.cs']:
        record(checks, 'Previous native repair code preserved: '+rel, (source/rel).read_bytes()==(previous/rel).read_bytes())
    old = json.loads((previous/'src/EscapeFromBorderlands.Client/LegendaryDefinitions.json').read_text())
    new = json.loads((source/'src/EscapeFromBorderlands.Client/LegendaryDefinitions.json').read_text())
    for kind in ['variants', 'legacyAliases']:
        record(checks, 'All previous raw '+kind+' identities retained', {v['templateId'] for v in old[kind]} <= {v['templateId'] for v in new[kind]})
    inspection = (source/'src/EscapeFromBorderlands.Client/ItemDescriptionPatch.cs').read_text()
    record(checks, 'Native inspection retains typed fields, Examined gate and guarded logging', all(s in inspection for s in ['typeof(ItemSpecificationPanel), "_item"', 'typeof(ItemSpecificationPanel), "_itemLabels"', 'typeof(ItemSpecificationPanel), "Examined"', 'binding.Examined', 'ReportFailure("description binding", error)', 'catch { /* Logging']))
    record(checks, 'No unresolved source or test merge markers', not any(re.search(r'^(<<<<<<<|=======|>>>>>>>)', p.read_text(), re.M) for parent in ['src','tests'] for p in (source/parent).rglob('*.cs')))
    failures = report('preservation', checks)
    audio = source/'src/EscapeFromBorderlands.Client/Audio/HolyHandGrenade.wav'
    payload = audio.read_bytes()
    sha = hashlib.sha256(payload).hexdigest()
    with wave.open(io.BytesIO(payload)) as w:
        details = dict(channels=w.getnchannels(), sampleRate=w.getframerate(), bitsPerSample=8*w.getsampwidth(), frames=w.getnframes(), durationSeconds=w.getnframes()/w.getframerate(), compression=w.getcomptype())
    checks = []
    record(checks, 'Audio bytes match upstream and published provenance', payload==(baseline/audio.relative_to(source)).read_bytes() and sha=='34c0735008126d344d5e9e4d138deccbecdc1cc7e0de9a42b0f104205a3db1ae' and sha in audio.with_name('README.md').read_text())
    record(checks, 'Bundled cue is 2.5-second stereo 44100-Hz 16-bit PCM', details==dict(channels=2, sampleRate=44100, bitsPerSample=16, frames=110250, durationSeconds=2.5, compression='NONE'))
    a = Assembly(args.client_dll)
    pe = a.u4(0x3c); opt = pe+24; dd=opt+(112 if a.u2(opt)==0x20b else 96)
    cli = a.offset(a.u4(dd+14*8)); resourcebase=a.offset(a.u4(cli+24))
    row = next(row for row in a.rows(40) if row['Name']=='EscapeFromBorderlands.Client.Audio.HolyHandGrenade.wav')
    start = resourcebase+row['Offset']; size=a.u4(start)
    record(checks, 'Validated client DLL embeds the complete cue under its exact resource name', a.data[start+4:start+4+size]==payload)
    game = Assembly(args.references/'Client/Assembly-CSharp.dll')
    sound = Assembly(args.references/'Client/UnityEngine.AudioModule.dll')
    def signature(a, name, method, sig):
        tid = a.find('^'+re.escape(name)+'$')[0][0]
        return any(row['Name']==method and a.signature(6,rid)==sig for rid,row in a.members(tid,6))
    record(checks, 'Native AudioClip.Create matches the reflective 5-argument constructor path', signature(sound, 'UnityEngine.AudioClip', 'Create', 'UnityEngine.AudioClip (string, int, int, int, bool)'))
    record(checks, 'Native AudioClip.SetData accepts float samples and returns success', signature(sound, 'UnityEngine.AudioClip', 'SetData', 'bool (float[], int)'))
    record(checks, 'Native BetterSource.Play matches both clip slots and playback flags', signature(game, 'BetterSource', 'Play', 'void (UnityEngine.AudioClip, UnityEngine.AudioClip, float, float, bool, bool)'))
    record(checks, 'Native source-group interface exposes the mixer used by pooled cue playback', signature(game, 'BetterAudio+ISourceGroup', 'get_MixerGroup', 'UnityEngine.Audio.AudioMixerGroup ()'))
    record(checks, 'Audio decoder source is byte-preserved from upstream', (source/'src/EscapeFromBorderlands.Client/WavePcm.cs').read_bytes()==(baseline/'src/EscapeFromBorderlands.Client/WavePcm.cs').read_bytes())
    failures += report('audio-asset', checks, audio=details, embeddedSha256=sha)
    return int(bool(failures))


if __name__=='__main__':
    raise SystemExit(main())
