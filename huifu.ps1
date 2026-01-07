$mouse = Get-PnpDevice | Where-Object {$_.Class -eq "Mouse" -and $_.FriendlyName -ne $null}

if ($mouse) {
    Enable-PnpDevice -InstanceId $mouse.InstanceId -Confirm:$false
    Write-Host "? 已恢复鼠标: $($mouse.FriendlyName)"
} else {
    Write-Host "? 未找到鼠标设备"
}