Option Strict On
Option Explicit On

Imports System.Diagnostics

Public NotInheritable Class TesseractRuntime
    Private Sub New()
    End Sub

    Public Shared Function FindExecutable() As String
        Dim paths = {
            IO.Path.Combine(AppContext.BaseDirectory, "tesseract", "tesseract.exe"),
            "C:\Program Files\Tesseract-OCR\tesseract.exe",
            "C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"
        }
        Return paths.FirstOrDefault(AddressOf IO.File.Exists)
    End Function

    Public Shared Sub Configure(startInfo As ProcessStartInfo, executable As String)
        Dim bundledDirectory = IO.Path.GetDirectoryName(executable)
        Dim tessdata = IO.Path.Combine(bundledDirectory, "tessdata")
        If IO.File.Exists(IO.Path.Combine(tessdata, "eng.traineddata")) Then
            startInfo.Environment("TESSDATA_PREFIX") = tessdata
        End If
    End Sub
End Class
