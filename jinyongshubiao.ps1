$mouse = Get-PnpDevice | Where-Object {$_.Class -eq "Mouse" -and $_.FriendlyName -ne $null}

if ($mouse) {
    Disable-PnpDevice -InstanceId $mouse.InstanceId -Confirm:$false
    Write-Host "? 已禁用鼠标: $($mouse.FriendlyName)"
} else {
    Write-Host "? 未找到鼠标设备"
}