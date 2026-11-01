"""Check the current mod release declarations; historical evidence is excluded."""
from pathlib import Path
import json, re, sys, xml.etree.ElementTree as ET

root=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else Path(__file__).resolve().parents[1]/'EscapeFromBorderlands'
semver=r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)'
versions={}
issues=[]
for kind in ['Client','Server']:
    path=root/f'src/EscapeFromBorderlands.{kind}/EscapeFromBorderlands.{kind}.csproj'
    props=ET.parse(path).getroot()
    version=props.findtext('./PropertyGroup/Version') or ''
    versions[kind+' project']=version
    if not re.fullmatch(semver,version):issues.append(kind+' project must declare MAJOR.MINOR.PATCH without leading zeroes')
    for name in ['AssemblyVersion','FileVersion','InformationalVersion']:
        if props.findtext('./PropertyGroup/'+name)!='$(Version)':issues.append(kind+' '+name+' must follow $(Version)')
    if props.findtext('./PropertyGroup/IncludeSourceRevisionInInformationalVersion')!='false':issues.append(kind+' informational version must not acquire a separate source suffix')
sources={
    'BepInEx plugin':(root/'src/EscapeFromBorderlands.Client/Plugin.cs',r'const string PluginVersion\s*=\s*"([^"]+)"'),
    'SPT mod metadata':(root/'src/EscapeFromBorderlands.Server/ModMetadata.cs',r'SemVersion Version[^\n]*new\("([^"]+)"'),
    'Build script':(root/'build.ps1',r'Escape From Borderlands ([0-9.]+) built'),
    'README title':(root/'README.md',r'^# Escape From Borderlands ([0-9.]+)\b')
}
for label,(path,pattern) in sources.items():
    match=re.search(pattern,path.read_text(),re.MULTILINE)
    versions[label]=match.group(1) if match else ''
expected=versions['Client project']
for label,value in versions.items():
    if value!=expected:issues.append(label+' does not match '+expected+': '+repr(value))
result=dict(version=expected,format='MAJOR.MINOR.PATCH',declarations=versions,errors=issues,
            note='Historical release records and SPT/dependency compatibility versions are independent of the current mod release.')
print(json.dumps(result,indent=2))
raise SystemExit(bool(issues))
