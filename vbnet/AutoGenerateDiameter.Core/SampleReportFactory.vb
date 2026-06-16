Option Strict On
Option Explicit On

Public NotInheritable Class EmptyReportFactory
    Private Sub New()
    End Sub

    Public Shared Function Create() As ImportedReport
        Dim sheet As New SheetData With {
            .PageNumber = 1,
            .TotalPages = 1
        }

        Return New ImportedReport With {.Sheets = New List(Of SheetData) From {sheet}}
    End Function
End Class
