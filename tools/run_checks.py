"""Run the portable SPT 4.1.6 regression and native-reference checks."""
import argparse
import hashlib
import concurrent.futures
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--references', type=Path, required=True)
    parser.add_argument('--server-directory', type=Path, required=True)
    parser.add_argument('--client-dll', type=Path, required=True)
    parser.add_argument('--dotnet', default=shutil.which('dotnet'))
    parser.add_argument('--output', type=Path)
    parser.add_argument('--suite', action='append', default=[])
    parser.add_argument('--uia-dll', type=Path, help='Optional supplied UIA 2.1.6 DLL for metadata/composition audits')
    args = parser.parse_args()
    if not args.dotnet:
        parser.error('Install the .NET 10 SDK or pass --dotnet.')
    project = Path(__file__).resolve().parents[1]
    refs, server, client = (p.resolve() for p in (args.references, args.server_directory, args.client_dll))
    output = (args.output or project / 'output/validation').resolve()
    output.mkdir(parents=True, exist_ok=True)
    for script in ['Latest/fixes/extract_production.py', 'Latest/combat/extract_gates.py', 'Latest/rigs/extract_adapters.py', 'Latest/grenades/extract_adapter.py', 'Latest/spawns/extract_standalone.py', 'Latest/earpieces/extract_adapter.py']:
        subprocess.run([sys.executable, str(project / 'tests' / script)], check=True, cwd=project)
    subprocess.run([sys.executable, str(project / 'tests/PresentationEffects/extract_production.py'), '--source', str(project / 'src/EscapeFromBorderlands.Client/BackpackPresentation.cs')], check=True, cwd=project)
    jobs = [
        ('fixes', 'Latest/fixes/FixTests.csproj', []),
        ('versions', 'Latest/versioning/VersionContracts.csproj', ['0.29.1', str(client), str(server/'EscapeFromBorderlands.Server.dll')]),
        ('combat', 'Latest/combat/CombatTests.csproj', []),
        ('fireball', 'BackpackFireball/FireballTests.csproj', []),
        ('storage', 'Latest/storage/StorageTests.csproj', []),
        ('layout', 'Latest/layout/LayoutTests.csproj', []),
        ('navigation', 'Latest/navigation/NavigationTests.csproj', []),
        ('performance', 'Latest/navigation/performance/PerfTests.csproj', []),
        ('visitor', 'Latest/visitor/VisitorTests.csproj', []),
        ('rigs', 'Latest/rigs/RigTests.csproj', []),
        ('grenades', 'Latest/grenades/GrenadeTests.csproj', []),
        ('uia-order', 'UiaOrder/UiaOrder.csproj', []),
        ('spawns', 'Latest/spawns/SpawnTests.csproj', []),
        ('quests', 'Latest/quests/QuestTests.csproj', []),
        ('earpieces', 'Latest/earpieces/EarpieceTests.csproj', []),
        ('explosionfx', 'Latest/explosionfx/ExplosionFxTests.csproj', []),
        ('hollywoodfx', 'Latest/explosionfx/ExplosionFxTests.csproj', []),
        ('backpack-width', 'Latest/backpack-width/BackpackWidthAudit.csproj', [str(refs / 'Database/items.json'), str(output / 'backpack-width')]),
        ('inspection', 'Inspection/Inspection.csproj', [str(output / 'inspection.json')]),
        ('presentation', 'PresentationEffects/PresentationEffects.csproj', [str(output / 'presentation.json')]),
        ('melee', 'MeleeAudit/MeleeAudit.csproj', [str(output / 'melee.json')]),
        ('server', 'ServerIntegration/ServerIntegration.csproj', [str(refs), str(output / 'server.json'), str(server)]),
        ('native-bindings', 'NativeBindings/NativeBindings.csproj', [str(refs), str(client), str(output / 'native-bindings.json')]),
        ('native-port-audit', 'NativePortAudit/NativePortAudit.csproj', [str(refs), str(client), str(project), str(output / 'native-port-audit.json')]),
    ]
    if args.uia_dll:
        optional = str(args.uia_dll.resolve())
        jobs += [
            ('native-bindings-uia', 'NativeBindings/NativeBindings.csproj', [str(refs),str(client),str(output/'native-bindings-uia.json'),optional]),
            ('native-port-audit-uia', 'NativePortAudit/NativePortAudit.csproj', [str(refs),str(client),str(project),str(output/'native-port-audit-uia.json'),optional]),
        ]
    def input_snapshot():
        result = {}
        for folder in ['src', 'tests']:
            for path in (project / folder).rglob('*'):
                rel = path.relative_to(project)
                if path.is_file() and path.suffix in ['.cs', '.csproj', '.json', '.wav', '.py'] and not {'bin','obj','__pycache__'}.intersection(rel.parts):
                    result[rel.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
        return result
    inputs = input_snapshot()
    env = os.environ.copy()
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1')
    def run(job):
        name, relative, arguments = job
        command = [args.dotnet, 'run', '--project', str(project / 'tests' / relative), '-c', 'Release',
                   '-p:NuGetAudit=false', '-p:UseSharedCompilation=false', '-p:ReferenceRoot=' + str(refs),
                   '-p:ReferencesRoot=' + str(refs), '-p:EfbClientReferenceRoot=' + str(refs / 'Client'),
                   '-p:ModServerRoot=' + str(server)]
        if arguments:
            command += ['--', *arguments]
        if name == 'hollywoodfx':
            command += ['-p:EfbHfx=true']
        process = subprocess.run(command, cwd=project, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        (output / (name + '.log')).write_text(process.stdout)
        if name in ('grenades', 'spawns', 'quests') and '0 failures' not in process.stdout:
            process.returncode = process.returncode or 1
        print(name, 'PASS' if process.returncode == 0 else 'FAIL', flush=True)
        return {'name': name, 'exit': process.returncode}
    unknown = set(args.suite) - {job[0] for job in jobs}
    if unknown:
        parser.error('Unknown suite: ' + ', '.join(sorted(unknown)))
    selected = [job for job in jobs if not args.suite or job[0] in args.suite]
    serial = {'hollywoodfx', 'native-bindings-uia', 'native-port-audit-uia'}
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        results = list(executor.map(run, [job for job in selected if job[0] not in serial]))
    results += [run(job) for job in selected if job[0] in serial]
    assert inputs == input_snapshot(), 'Source/test inputs changed during the run'
    if not args.suite:
        (output / 'test-input-sha256.json').write_text(json.dumps(inputs, indent=2) + '\n')
    report = output / 'suite-runs.json'
    retained = json.loads(report.read_text()) if args.suite and report.exists() else []
    by_name = {row['name']: row for row in [*retained, *results]}
    report.write_text(json.dumps([by_name[job[0]] for job in jobs if job[0] in by_name], indent=2))
    return int(any(row['exit'] for row in results))

if __name__ == '__main__':
    raise SystemExit(main())
