# 白底可见性取证：在悬浮球背后放一张纯白窗口，再对球体做逐像素扫描
# 用法（先启动 AssistiveTouch.exe）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File artifacts/verify-white-bg.ps1
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Win {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static RECT FindBall(uint pid) {
    RECT found = new RECT(); bool ok = false;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      int ex = GetWindowLong(h, -20);
      if (p == pid && IsWindowVisible(h) && (ex & 0x8) != 0) {   // 可见 + WS_EX_TOPMOST
        GetWindowRect(h, out found); ok = true; return false;
      }
      return true;
    }, IntPtr.Zero);
    if (!ok) throw new Exception("悬浮球窗口未找到（进程在跑吗？）");
    return found;
  }
}
"@

$proc = Get-Process AssistiveTouch -ErrorAction SilentlyContinue
if (-not $proc) { "错误：AssistiveTouch 未运行"; exit 1 }

$r = [Win]::FindBall([uint32]$proc.Id)
$w = $r.R - $r.L; $h = $r.B - $r.T
"球窗口: ($($r.L),$($r.T))-($($r.R),$($r.B))  ${w}x${h}"

# 纯白背板窗口，居中垫在球后面（球是 Topmost，天然压在上面）
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.BackColor = [System.Drawing.Color]::White
$form.Bounds = New-Object System.Drawing.Rectangle(($r.L - 150), ($r.T - 120), ($w + 300), ($h + 240))
$form.TopMost = $false
$form.Show()
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 400

$pad = 12
$bmp = New-Object System.Drawing.Bitmap(($w + 2*$pad), ($h + 2*$pad))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(($r.L - $pad), ($r.T - $pad), 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save("$PSScriptRoot\white-bg-probe.png", [System.Drawing.Imaging.ImageFormat]::Png)

# 扫描球心水平线，打印颜色变化点
$y = [int]($bmp.Height / 2)
$prev = ""
$changes = @()
for ($x = 0; $x -lt $bmp.Width; $x++) {
  $c = $bmp.GetPixel($x, $y)
  $key = "$($c.R),$($c.G),$($c.B)"
  if ($key -ne $prev) {
    $changes += ("x={0,3}  rgb({1})" -f $x, $key)
    $prev = $key
  }
}
"--- 球心水平扫描线（y=$y），共 $($changes.Count) 段 ---"
$changes | ForEach-Object { $_ }

# 计算描边的近似对比度（相对亮度比，WCAG 公式）
function Lum($r,$g,$b) {
  $f = { param($v) $s = $v/255.0; if ($s -le 0.03928) { $s/12.92 } else { [math]::Pow((($s+0.055)/1.055), 2.4) } }
  0.2126*(&$f $r) + 0.7152*(&$f $g) + 0.0722*(&$f $b)
}
$stroke = $changes | Where-Object { $_ -match 'rgb\((\d+),' -and [int]$Matches[1] -lt 160 } | Select-Object -First 1
if ($stroke) {
  $m = [regex]::Match($stroke, 'rgb\((\d+),(\d+),(\d+)\)')
  $l1 = 1.0; $l2 = Lum ([int]$m.Groups[1].Value) ([int]$m.Groups[2].Value) ([int]$m.Groups[3].Value)
  $ratio = ($l1 + 0.05) / ($l2 + 0.05)
  "描边取样: $stroke  -> 相对纯白对比度 {0:N2}:1  (WCAG AA 正文要求 4.5:1，图形 3:1)" -f $ratio
} else {
  "警告：扫描线上未找到深色描边（<160），轮廓可能缺失"
}

$form.Close()
$form.Dispose()
"截图已保存: $PSScriptRoot\white-bg-probe.png"
