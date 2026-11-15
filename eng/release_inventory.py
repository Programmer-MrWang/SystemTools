#!/usr/bin/env python3
"""Generate distribution metadata from the feature ledger and resolved package inputs."""
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET
import m1

POLICY = json.loads((m1.ROOT / 'eng/release-policy.json').read_text(encoding='utf-8'))
SHARED = '''ActionFlowExecutionConfirmation BackgroundPlayAudio BlackScreenHtml ActionInProgressTrigger MainWindowClickTrigger Copy Move Delete ToggleWorkflow LoadTemporaryClassPlan ClearAllNotifications OpenAppSettings OpenProfileEditor OpenClassSwapWindow TriggerCustomTrigger ReadFile NetworkStatus ClipboardContent LocalQuote NextClassDisplay BetterCarouselContainer ScrollingText ProcessRunningRule UsingClassPlanRule UsingTimeLayoutRule InTimePeriodRule'''.split()
NATIVE = '''AltF4 AltTab CtrlZ TypeContent SimulateMouse KillProcess Shutdown AdvancedShutdown CancelShutdown ImmediateShutdown ImmediateRestart Sleep LockScreen ShowDesktop SwitchTheme SwitchSystemAccentColor SetVolume LongIdleTrigger CloneDisplay ExtendDisplay InternalDisplay ExternalDisplay AdjustScreenBrightness AutoSwitchClassIslandTheme AutoHideMainWindowWhenOccluded ScreenShot UsbDeviceTrigger AutoOpenUsbDriveOnInsert SimulateKeyboard SimulateKeyCombination EnterKey EscKey F11Key HotkeyTrigger WindowOperation ShowFloatingWindow ToggleFloatingWindowLayer SwitchFloatingWindowTheme ToggleFloatingWindowProfile FloatingWindowTrigger ChangeWallpaper CameraCapture MediaMusicPlayingRule LyricsDisplay KeywordTrigger ShowAiChatDialog WakeUpVoiceConversationAi EnableVoiceWakeAi'''.split()


def capabilities():
    ledger = json.loads((m1.ROOT / 'src/SystemTools.Contracts/Features.json').read_text(encoding='utf-8'))
    ids = [item['Id'] for item in ledger]
    if len(ids) != len(set(ids)): raise RuntimeError('Feature ledger has duplicate IDs.')
    if not {'SystemTools.' + key for key in SHARED + NATIVE}.issubset(ids): raise RuntimeError('Release support map refers to unknown feature IDs.')
    rows = []
    for item in ledger:
        suffix = item['Id'].split('.', 1)[1]
        states = {rid: '未接入' for rid in POLICY['runtimeIds']}
        states['win-x64'] = '保留实现；需设置与现场条件'
        if suffix in SHARED:
            for rid in ('linux-x64', 'osx-arm64'): states[rid] = '共享实现；需设置与现场条件'
        if suffix in NATIVE:
            for rid in ('linux-x64', 'osx-arm64'): states[rid] = '原生接入；需权限/依赖/设备'
        if suffix in ('EnableDevice','DisableDevice','EnableMouse','DisableMouse','RestartAsAdmin','ShowToast'):
            for rid in ('linux-x64','osx-arm64'): states[rid]='Windows 专属；本平台不提供对应入口'
        if suffix in ('Shutdown', 'AdvancedShutdown', 'CancelShutdown'):
            for rid in ('linux-x64', 'osx-arm64'): states[rid] = '插件计时；宿主退出取消；系统授权后执行'
        if suffix == 'ShowDesktop': states['osx-arm64'] = '无可靠通用公开接口'
        if suffix == 'SwitchSystemAccentColor':
            states['osx-arm64'] = '无任意系统强调色公开写入接口'
            states['linux-x64'] = 'GNOME 公开固定色板；其他桌面未开放'
        if suffix == 'SwitchTheme': states['linux-x64'] = 'GNOME 公开 color-scheme；其他桌面未开放'
        if suffix in ('AltF4','AltTab','CtrlZ'): states['osx-arm64'] = '映射为 Command+W / Command+Tab / Command+Z 对应系统语义'
        if suffix == 'MainWindowClickTrigger':
            for rid in ('linux-x64', 'osx-arm64'): states[rid] = '主界面实际点击；穿透模式不触发'
        if suffix in ('AutoSwitchClassIslandTheme', 'AutoHideMainWindowWhenOccluded'): states['linux-x64'] = '不支持可靠排除自身捕获'
        if suffix in ('InternalDisplay', 'ExternalDisplay'): states['osx-arm64'] = '公共 API 不提供关闭单屏'
        if suffix in ('LyricsDisplay', 'MediaMusicPlayingRule'): states['osx-arm64'] = '未提供通用媒体会话'
        if suffix in ('FullscreenClock', 'ClickSimulation'):
            states = {rid: ('产品移除' if suffix == 'FullscreenClock' else '原未注册；不新增入口') for rid in POLICY['runtimeIds']}
        rows.append(dict(item, platforms=states, runtimeVerified=False))
    return dict(schemaVersion=1, pluginId=POLICY['pluginId'], version=POLICY['version'], channel=POLICY['channel'],
                featureLedgerSha256=m1.sha(m1.ROOT / 'src/SystemTools.Contracts/Features.json'), features=rows,
                note='Static implementation coverage, not live probes or hardware acceptance. Settings and runtime FeatureCatalog are authoritative.')


def support_markdown(report):
    lines = ['# SystemTools 3.0 平台能力表', '', '由源码功能台账生成；实现接入不等于设备、权限或对应平台已经验收。运行时设置页的实际探测为准。', '',
             '| 稳定 ID | 名称 | Windows x64 | Linux x64 X11 | macOS ARM64 |', '| --- | --- | --- | --- | --- |']
    for row in report['features']:
        cells = [row['Id'], row['Name']] + [row['platforms'][rid] for rid in POLICY['runtimeIds']]
        lines.append('| ' + ' | '.join(str(value).replace('|', '\\|') for value in cells) + ' |')
    lines += ['', '系统认证：Windows 使用既有系统认证，Mac 使用本人 LocalAuthentication；Linux 不开放。Mac 使用原生系统玻璃/材质，旧系统回落，Linux 材质只提供可靠能力。', '',
              '选配依赖：' + '、'.join(POLICY['optionalDependencies']) + '。模型、授权、外屏与背光设备均不随构建自动具备。', '',
              '完整实体设备、Windows Hello、外部 AI 服务、中文识别质量、多屏/热插拔、Linux 桌面和正式签名升级仍分别追踪。']
    return '\n'.join(lines) + '\n'


def legal_inventory(stage, rid):
    stage = Path(stage); output = stage / 'release'; output.mkdir(exist_ok=True)
    assets_paths = [m1.ROOT / 'src/SystemTools/obj/project.assets.json', m1.ROOT / 'src/SystemTools.SharedHost/obj/project.assets.json',
                    m1.ROOT / ('src/SystemTools.Platforms.' + m1.MODULES[rid]) / 'obj/project.assets.json']
    if rid == 'win-x64': assets_paths.append(m1.ROOT / 'VoskWorker/obj/project.assets.json')
    packages = {}
    for assets_path in assets_paths:
        assets = json.loads(assets_path.read_text(encoding='utf-8'))
        for key, library in assets['libraries'].items():
            if library['type'] != 'package' or key in packages: continue
            folder = next((Path(base) / library['path'] for base in assets['packageFolders'] if (Path(base) / library['path']).is_dir()), None)
            if folder is None: raise RuntimeError('Missing resolved legal metadata: ' + key)
            nuspec = next(folder.glob('*.nuspec')); metadata = ET.parse(nuspec).getroot()
            def value(name):
                node = next((e for e in metadata.iter() if e.tag.rsplit('}', 1)[-1] == name), None)
                return node.text if node is not None else None
            legal = sorted([path for path in folder.rglob('*') if path.is_file() and
                (path.name.casefold().startswith(('license', 'notice', 'thirdpartynotice')) or path == nuspec or path.name == 'source-provenance.json')])
            paths = []
            safe = re.sub(r'[^a-zA-Z0-9._-]', '_', key)
            for index, path in enumerate(legal):
                relative = 'release/licenses/' + rid + '/' + safe + '/' + str(index) + '-' + path.name + '.txt'
                target = stage / relative; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(path.read_bytes())
                paths.append(dict(packagePath=path.relative_to(folder).as_posix(), file=relative, sha256=m1.sha(target)))
            packages[key] = dict(id=key.rsplit('/', 1)[0], version=key.rsplit('/', 1)[1], sha512=library.get('sha512'),
                license=value('license'), licenseUrl=value('licenseUrl'), copyright=value('copyright'), projectUrl=value('projectUrl'), notices=paths)
    m1.write_json(output / ('licenses-' + rid + '.json'), dict(schemaVersion=1, runtimeId=rid, packages=list(packages.values()),
        scope='Full resolved dependency graph including host-owned build references; metadata is not a claim that every package is redistributed.'))
    for source, name in [(m1.ROOT / 'LICENSE', 'SystemTools-LICENSE.txt'),
                         (m1.ROOT / 'ThirdParty/LiquidGlassAvaloniaUI/LICENSE', 'LiquidGlassAvaloniaUI-LICENSE.txt')]:
        target = stage / 'release/licenses' / rid / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(source.read_bytes())
    return packages


def generate(stage, rid):
    stage = Path(stage); output = stage / 'release'; output.mkdir(exist_ok=True)
    m1.write_json(output / 'policy.json', POLICY)
    report = capabilities(); m1.write_json(output / 'capabilities.json', report)
    (output / 'platform-support.md').write_bytes(support_markdown(report).encode('utf-8'))
    legal_inventory(stage, rid)
    return report
