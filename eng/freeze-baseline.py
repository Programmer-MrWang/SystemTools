#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Read the frozen Git tree, never personal config files. Reproducible M0 ledger."""
from pathlib import Path
import subprocess, json, re, hashlib, collections

ROOT = Path(__file__).resolve().parents[1]
BASE = '10a39f9db130ecaf38158e7fad8808dc3416acc3'
def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)
paths = git('ls-tree', '-r', '--name-only', BASE).decode().splitlines()
def read_source(path):
    raw = git('show', BASE + ':' + path)
    try:
        return raw.decode('utf-8-sig')
    except UnicodeDecodeError:
        return raw.decode('gb18030')  # A few original Windows files use GBK.
sources = {p: read_source(p) for p in paths if p.endswith('.cs')}
plugin = sources['Plugin.cs']
features = []
for path, source in sources.items():
    for match in re.finditer(r'\[(ActionInfo|TriggerInfo|ComponentInfo|AuthorizeProviderInfo|SettingsPageInfo)\s*\((.*?)\)\s*\]', source, re.S):
        values = re.findall(r'"((?:\\.|[^"\\])*)"', match[2])
        if len(values) < 2:
            raise ValueError(path)
        kind = dict(ActionInfo='action', TriggerInfo='trigger', ComponentInfo='component', AuthorizeProviderInfo='authorizer', SettingsPageInfo='settingsPage')[match[1]]
        tail = source[match.end():]
        typename = re.search(r'class\s+(\w+)', tail)[1]
        settings = re.search(r'(?:ActionBase|TriggerBase|ComponentBase|AuthorizeProviderControlBase)\s*<\s*(\w+)', tail)
        st = settings[1] if settings else None
        permissions = []
        for pattern, name in [('Device|Mouse|Keyboard|Hotkey|Simulate|CtrlZ|TypeContent', '输入/设备/窗口权限'), ('Shutdown|Restart|Hibernate|Sleep|LockScreen', '会话/电源权限，仅在测试机验证'), ('Camera|Face', '相机/硬件/识别模型'), ('ScreenShot|Capture|Occlusion', '屏幕/窗口采集'), ('ReadFile|Copy|Move|Delete|Wallpaper', '文件权限和路径'), ('Ai|Voice|Keyword', '网络/麦克风/密钥，按功能启用')]:
            if re.search(pattern, typename):
                permissions.append(name)
        dependencies = [name for token, name in [('ffmpeg', 'FFmpeg 扩展'), ('System.Speech', 'Windows SAPI'), ('Vosk', 'Vosk Worker/模型'), ('Dlib', 'Dlib/OpenCV/模型'), ('OpenCv', 'OpenCV'), ('WindowsHello', 'Windows Hello'), ('SuperAutoIsland', 'SAI 可选接口')] if token.lower() in source.lower()]
        features.append(dict(kind=kind, id=values[0], name=values[1], type=typename, source=path, line=source[:match.start()].count('\n') + 1, settingsType=st, settingsSources=[p for p, s in sources.items() if st and re.search(r'\bclass\s+' + st + r'\b', s)], registrationLines=[i + 1 for i, line in enumerate(plugin.splitlines()) if typename in line], revert='OnRevert' in source, interrupt='OnInterrupted' in source or 'InterruptCancellationToken' in source, dependencies=dependencies, permissions=permissions, verification='开关/重启；新增/保存配置；执行；' + ('恢复初值；' if 'OnRevert' in source else '') + ('运行中停止；' if 'Interrupt' in source else '') + '缺依赖/拒绝权限；退出重进；稳定 ID', runtimeStatus='Windows 实机待验证', platformTargets={'Windows': 'M2 保留并迁入', 'Linux X11': '后续逐项实现/验收', 'macOS arm64': '后续逐项实现/验收'}, policy='用户指定不迁移沉浸式时钟' if typename == 'FullscreenClockAction' else '保留原行为'))
for match in re.finditer(r'services\.AddRule<([^,>]+),\s*([^>]+)>\(\s*"([^"]+)",\s*"([^"]+)"', plugin):
    features.append(dict(kind='rule', id=match[3], name=match[4], type=match[1], settingsType=match[1], source='Plugin.cs', line=plugin[:match.start()].count('\n') + 1, verification='启用/禁用；真假分支；保存重进；目标/依赖缺失', runtimeStatus='Windows 实机待验证'))
for match in re.finditer(r'new ThemeManifest\s*\{(.*?)\n\s*\}', plugin, re.S):
    values = dict(re.findall(r'(Id|Name)\s*=\s*"([^"]+)"', match[1]))
    features.append(dict(kind='theme', id=values['Id'], name=values['Name'], source='Plugin.cs', line=plugin[:match.start()].count('\n') + 1, verification='列表/启用；横竖布局；重启；旧 avares URI；截图对照', runtimeStatus='Windows 实机待验证'))
for identity, name, source, verify in [('AI', 'AI 对话/课程档案/行动工具/语音', 'Services/AiChatWindowService.cs', '密钥保留；会话保存；中断/审批；语音争用；网络失败'), ('SAI', 'SAI/Blockly 接入', 'Plugin.cs', 'SAI 已装/未装；接口身份；能力目录'), ('Excel', '课程表 Excel 导出', 'Services/ClassIslandProfileExcelExporter.cs', '导出/打开；排版；NPOI 缺失'), ('Tray', '托盘/快捷入口', 'Plugin.cs', '菜单；窗口；重复启动；退出清理'), ('OptionalDependencies', '扩展下载/FFmpeg/人脸/Vosk', 'Shared/DependencyPaths.cs', '空目录/模型缺失/损坏/下载失败'), ('Exit', '普通退出取消关机计划', 'Plugin.cs', 'Windows 普通退出无条件 shutdown /a，按用户要求保持')]:
    features.append(dict(kind='integration', id='SystemTools.' + identity, name=name, source=source, verification=verify, runtimeStatus='Windows 实机待验证'))
contracts = []
for path, source in sources.items():
    if not path.startswith(('Settings/', 'ConfigHandlers/', 'Models/ComponentSettings/', 'Rules/')):
        continue
    contracts.append(dict(source=path, properties=[dict(type=t.strip(), name=n) for t, n in re.findall(r'\b(?:public|internal)\s+(?:static\s+)?([\w<>?,.\[\] ]+)\s+(\w+)\s*(?:\{|=>)', source)], explicitJsonNames=re.findall(r'JsonPropertyName\("([^"]+)"\)', source), defaultExpressions=[line.strip() for line in source.splitlines() if re.search(r'(?:_[A-Za-z]\w*\s*=|\{\s*get;\s*set;\s*\}\s*=)', line)], defaultNote='保留源码表达式；未在 Windows 实例化。未初始化字段遵循 CLR 默认值；条件/计算默认值需参照原源码。'))
out = ROOT / 'docs/baseline/m0'
out.mkdir(parents=True, exist_ok=True)
def dump(name, value):
    (out / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')
counts = dict(collections.Counter(f['kind'] for f in features))
dump('windows-features.json', dict(schemaVersion=1, sourceCommit=BASE, extraction='属性标记+Plugin.cs 注册；静态证据，不等于实机验证。registrationLines 指向条件分支。', counts=counts, features=features))
dump('settings-contracts.json', dict(sourceCommit=BASE, contracts=contracts))
dump('source-hashes.json', {p: hashlib.sha256(git('show', BASE + ':' + p)).hexdigest() for p in paths if p.endswith(('.cs', '.csproj', '.axaml', '.yml', '.json')) and not p.startswith(('.idea/', '.tang/'))})
rows = ['# M0 Windows 功能验收台账', '', '基线 `' + BASE + '`。由 eng/freeze-baseline.py 生成。Windows 实机均待验收；字段与默认表达式见 settings-contracts.json。', '', '| 分类 | 稳定 ID | 功能 | 源码 | 恢复/中断 | 验证 |', '| --- | --- | --- | --- | --- | --- |']
for f in features:
    rows.append('| ' + ' | '.join([f['kind'], '`' + f['id'] + '`', f['name'], f['source'] + ':' + str(f.get('line', 1)), str(f.get('revert', '—')) + '/' + str(f.get('interrupt', '—')), f['verification']]) + ' |')
(out / 'windows-checklist.md').write_text('\n'.join(rows) + '\n')
print('Frozen:', counts, '; settings contracts:', len(contracts))
