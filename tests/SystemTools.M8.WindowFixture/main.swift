import AppKit
import Foundation

let root: URL
if CommandLine.arguments.count > 1 { root = URL(fileURLWithPath: CommandLine.arguments[1]) }
else if Bundle.main.bundleURL.pathExtension == "app" { root = Bundle.main.bundleURL.deletingLastPathComponent() }
else { fputs("Fixture requires an isolated output directory.\n", stderr); exit(64) }
try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
let app=NSApplication.shared
app.setActivationPolicy(.regular)
let menu = NSMenu()
let applicationItem = NSMenuItem();menu.addItem(applicationItem)
let applicationMenu = NSMenu();applicationMenu.addItem(withTitle:"退出夹具",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q")
applicationItem.submenu=applicationMenu
let editItem=NSMenuItem();menu.addItem(editItem)
let editMenu=NSMenu(title:"编辑")
editMenu.addItem(withTitle:"撤销",action:Selector(("undo:")),keyEquivalent:"z")
let redo=NSMenuItem(title:"重做",action:Selector(("redo:")),keyEquivalent:"Z");editMenu.addItem(redo)
editMenu.addItem(withTitle:"全选",action:#selector(NSText.selectAll(_:)),keyEquivalent:"a")
editItem.submenu=editMenu
app.mainMenu=menu
let window=NSWindow(contentRect:NSRect(x:220,y:220,width:600,height:380),styleMask:[.titled,.closable,.resizable,.miniaturizable],backing:.buffered,defer:false)
window.title="SystemTools M8 输入夹具"
let scroll=NSScrollView(frame:NSRect(x:20,y:70,width:560,height:280))
scroll.hasVerticalScroller=true
let text=NSTextView(frame:scroll.bounds)
text.isRichText=false;text.allowsUndo=true;text.font=NSFont.systemFont(ofSize:20)
text.setAccessibilityIdentifier("M8Input")
scroll.documentView=text
let button=NSButton(title:"记录点击",target:nil,action:nil)
button.frame=NSRect(x:20,y:20,width:180,height:36)
var hits=0
final class ClickTarget:NSObject {
    @objc func click(){hits += 1}
}
let target=ClickTarget();button.target=target;button.action=#selector(ClickTarget.click)
window.contentView!.addSubview(scroll);window.contentView!.addSubview(button)
final class FixtureAppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        window.makeKeyAndOrderFront(nil);window.makeFirstResponder(text)
        app.activate(ignoringOtherApps:true)
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication)->Bool {true}
}
let appDelegate=FixtureAppDelegate();app.delegate=appDelegate
Timer.scheduledTimer(withTimeInterval:0.2,repeats:true){_ in
    let command=root.appendingPathComponent("fixture-command")
    if let operation=try? String(contentsOf:command,encoding:.utf8) {
        try? FileManager.default.removeItem(at:command)
        if operation=="minimize-baseline" {window.miniaturize(nil)}
        if operation=="restore" {window.deminiaturize(nil);window.makeKeyAndOrderFront(nil);app.activate(ignoringOtherApps:true)}
    }
    let frame=window.frame
    let height=NSScreen.screens.first!.frame.height
    let position=button.convert(button.bounds,to:nil)
    let screen=window.convertToScreen(position)
    let state:[String:Any]=["text":text.string,"canUndo":text.undoManager?.canUndo ?? false,"clicks":hits,"minimized":window.isMiniaturized,"visible":window.isVisible,
        "x":frame.minX,"y":height-frame.maxY,"width":frame.width,"height":frame.height,
        "buttonX":screen.midX,"buttonY":height-screen.midY,"pid":getpid()]
    if let bytes=try? JSONSerialization.data(withJSONObject:state,options:[.sortedKeys]) {try? bytes.write(to:root.appendingPathComponent("fixture.json"),options:.atomic)}
    if FileManager.default.fileExists(atPath:root.appendingPathComponent("fixture-stop").path){app.terminate(nil)}
}
app.run()
