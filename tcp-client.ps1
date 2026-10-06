# Cliente de prueba para el socket TCP.
# Uso:  .\tcp-client.ps1 -Servidor 18.227.84.226 '{get:1}' '{insert:{"nombre":"Leche","precio":24.5,"categoriaId":1}}'
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Servidor = "localhost",
    [int]$Puerto = 6061,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Mensajes
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8   # para que se vean los acentos
$client = [System.Net.Sockets.TcpClient]::new($Servidor, $Puerto)
$stream = $client.GetStream()
$utf8 = [System.Text.UTF8Encoding]::new($false)
$writer = [System.IO.StreamWriter]::new($stream, $utf8); $writer.AutoFlush = $true; $writer.NewLine = "`n"
$reader = [System.IO.StreamReader]::new($stream, $utf8)

foreach ($m in $Mensajes) {
    Write-Host "> $m" -ForegroundColor Cyan
    $writer.WriteLine($m)
    Write-Host "< $($reader.ReadLine())"
}
$client.Close()
