# Deployed components. Order = start order (gateway last).
@{
    Services = @(
        @{ Name = 'Identity'; Port = 5105; Exe = 'SecurityManager.Identity.API.exe'; Project = 'backend\src\Services\Identity\SecurityManager.Identity.API'; Database = 'SecurityManager_Identity' }
        @{ Name = 'Alert';    Port = 5104; Exe = 'SecurityManager.Alert.API.exe';    Project = 'backend\src\Services\Alert\SecurityManager.Alert.API';       Database = 'SecurityManager_Alert' }
        @{ Name = 'Security'; Port = 5102; Exe = 'SecurityManager.Security.API.exe'; Project = 'backend\src\Services\Security\SecurityManager.Security.API'; Database = 'SecurityManager_Security' }
        @{ Name = 'Firewall'; Port = 5101; Exe = 'SecurityManager.Firewall.API.exe'; Project = 'backend\src\Services\Firewall\SecurityManager.Firewall.API'; Database = 'SecurityManager_Firewall' }
        @{ Name = 'Scanner';  Port = 5103; Exe = 'SecurityManager.Scanner.API.exe';  Project = 'backend\src\Services\Scanner\SecurityManager.Scanner.API';   Database = 'SecurityManager_Scanner' }
        @{ Name = 'Network';  Port = 5106; Exe = 'SecurityManager.Network.API.exe';  Project = 'backend\src\Services\Network\SecurityManager.Network.API';   Database = 'SecurityManager_Network' }
        @{ Name = 'Gateway';  Port = 5000; Exe = 'SecurityManager.Gateway.exe';      Project = 'backend\src\Gateway\SecurityManager.Gateway';                Database = $null }
        # Host enforcement agent: no HTTP port; must run as a Windows service under LocalSystem (manages Windows Firewall).
        @{ Name = 'Agent';    Port = $null; Exe = 'SecurityManager.Agent.exe';       Project = 'backend\src\Agent\SecurityManager.Agent';                   Database = $null; ServiceOnly = $true }
    )
}
