#!/usr/bin/env python3
"""Compile the real bridge generator host against an existing fork Editor DLL."""
# Copyright (c) 2026 mmm1h. Licensed under the MIT license in the repository root.
import argparse
import json
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet-sdk', type=Path, required=True,
                        help='Unity Editor/Data/DotNetSdk')
    parser.add_argument('--editor-assembly', type=Path, required=True,
                        help='HybridCLR.Editor.dll compiled from this fork')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    sdk, assembly, output = args.dotnet_sdk.resolve(), args.editor_assembly.resolve(), args.output.resolve()
    if output.exists():
        parser.error('output must not exist')
    csc = next((sdk / 'sdk').glob('10.*/Roslyn/bincore/csc.dll'), None)
    refs = next((sdk / 'packs/Microsoft.NETCore.App.Ref').glob('10.*/ref/net10.0'), None)
    if not csc or not refs or not assembly.is_file():
        parser.error('requires Unity 7 .NET 10 SDK and compiled fork Editor assembly')
    output.mkdir(parents=True)
    package = Path(__file__).resolve().parents[2]
    for source in [assembly, package / 'Plugins/dnlib.dll', assembly.parent / 'HybridCLR.Runtime.dll',
                   sdk.parent / 'Managed/UnityEngine/UnityEngine.CoreModule.dll']:
        if source.exists():
            shutil.copy2(source, output / source.name)
    options = ['-nologo', '-noconfig', '-nostdlib+', '-target:exe', '-langversion:latest',
               '-out:' + str(output / 'GenerateBridge.dll'),
               *['-r:' + str(path) for path in sorted(refs.glob('*.dll'))],
               '-r:' + str(assembly), '-r:' + str(package / 'Plugins/dnlib.dll'),
               str(Path(__file__).with_name('GenerateBridge.cs'))]
    rsp = output / 'program.rsp'
    rsp.write_text('\n'.join('"' + option + '"' for option in options), encoding='utf-8')
    subprocess.run([str(sdk / 'dotnet.exe'), str(csc), '@' + str(rsp)], check=True)
    (output / 'GenerateBridge.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net10.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}}}), encoding='utf-8')


if __name__ == '__main__':
    main()
