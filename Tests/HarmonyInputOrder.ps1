param(
    [string]$TankardsDll = "$PSScriptRoot\..\bin\Debug\UsefulTankards.dll",
    [string]$FishingDll,
    [Parameter(Mandatory = $true)]
    [string]$EpicLootDll,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
)
# Run with Windows PowerShell (.NET Framework). Uses the installed Harmony class
# processor on real patch types, but never calls Patch or any Unity/game method.
$ErrorActionPreference = 'Stop'
$script:referencePaths = @("$GamePath\BepInEx\core", "$GamePath\valheim_Data\Managed", (Split-Path $EpicLootDll))
$resolveAssembly = [ResolveEventHandler] {
    param($sender, $request)
    $name = [Reflection.AssemblyName]::new($request.Name).Name + '.dll'
    foreach ($directory in $script:referencePaths) {
        $candidate = Join-Path $directory $name
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::UnsafeLoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolveAssembly)
try {
    [void][Reflection.Assembly]::UnsafeLoadFrom("$GamePath\BepInEx\core\0Harmony.dll")
    $harmony = [HarmonyLib.Harmony]::new('UsefulTankards.Tests.MetadataOnly')
    function Read-PrefixPriority([string]$dll, [string]$typeName) {
        $assembly = [Reflection.Assembly]::UnsafeLoadFrom((Resolve-Path -LiteralPath $dll).Path)
        $type = $assembly.GetType($typeName, $true)
        $processor = $harmony.CreateClassProcessor($type)
        $flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
        $patches = $processor.GetType().GetField('patchMethods', $flags).GetValue($processor)
        foreach ($patch in $patches) {
            $info = $patch.GetType().GetField('info', $flags).GetValue($patch)
            if ($info.method.Name -eq 'Prefix') {
                $priority = if ($info.priority -eq -1) { 400 } else { $info.priority }
                Write-Host "$typeName effective Prefix priority: $priority"
                return $priority
            }
        }
        throw "Prefix missing: $typeName"
    }
    $epicPriority = Read-PrefixPriority $EpicLootDll 'EpicLoot.ShardStones.SocketsUI+InventoryGui_Update_Patch'
    $tankardPriority = Read-PrefixPriority $TankardsDll 'UsefulTankards.UsefulTankardsInventoryGuiUpdatePatch'
    if ($tankardPriority -le $epicPriority) { throw 'Tankard input does not precede EpicLoot after actual Harmony attribute merging' }
    if ($FishingDll) {
        $fishingPriority = Read-PrefixPriority $FishingDll 'TrollingFishing.InventoryGuiUpdateFishingRodBagInputPatch'
        if ($fishingPriority -le $epicPriority) { throw 'Fishing input does not precede EpicLoot after actual Harmony attribute merging' }
    }
    Write-Host 'PASS real Harmony class processing; no detours or Unity gameplay executed'
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolveAssembly) }
