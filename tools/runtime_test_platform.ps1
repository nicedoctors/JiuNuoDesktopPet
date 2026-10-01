param(
    [int]$Left = 800,
    [int]$Top = 700,
    [int]$Width = 820,
    [int]$Height = 320,
    [int]$LifetimeSeconds = 300
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$form = [System.Windows.Forms.Form]::new()
$form.Text = 'SoftMochiPet Runtime Test Platform'
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Location = [System.Drawing.Point]::new($Left, $Top)
$form.Size = [System.Drawing.Size]::new($Width, $Height)
$form.MinimumSize = [System.Drawing.Size]::new(240, 120)
$form.BackColor = [System.Drawing.Color]::FromArgb(34, 41, 58)
$form.ShowInTaskbar = $true

$label = [System.Windows.Forms.Label]::new()
$label.Dock = [System.Windows.Forms.DockStyle]::Fill
$label.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$label.ForeColor = [System.Drawing.Color]::White
$label.Font = [System.Drawing.Font]::new('Microsoft YaHei UI', 18)
$label.Text = "Soft Mochi runtime terrain platform`nMovable and closable"
$form.Controls.Add($label)

$timer = [System.Windows.Forms.Timer]::new()
$timer.Interval = [Math]::Max(1000, $LifetimeSeconds * 1000)
$timer.Add_Tick({
    $timer.Stop()
    $form.Close()
})
$form.Add_Shown({ $timer.Start() })
$form.Add_FormClosed({
    $timer.Dispose()
    $label.Dispose()
    $form.Dispose()
})

[System.Windows.Forms.Application]::Run($form)
