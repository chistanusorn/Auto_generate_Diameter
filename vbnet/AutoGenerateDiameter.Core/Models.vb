Option Strict On
Option Explicit On

Public NotInheritable Class Measurement
    Public Property TrayPosition As Integer
    Public Property RowNumber As Integer
    Public Property LotNumber As String = ""
    Public Property RDiameter As Double
    Public Property LDiameter As Double
    Public Property TrayNumber As String = ""
    Public Property RPresent As Boolean
    Public Property LPresent As Boolean
    Public Property RDash As Boolean
    Public Property LDash As Boolean
End Class

' A single table cell to OCR, expressed as normalized (0–1) coordinates of the
' source image plus its logical position. The imaging layer (WinForms) crops the
' region; the Core layer runs OCR. Keeping coordinates normalized lets Core stay
' free of any System.Drawing dependency.
Public NotInheritable Class CellRegion
    Public Property TrayPosition As Integer
    Public Property RowNumber As Integer
    Public Property Side As String = ""
    Public Property Left As Double
    Public Property Top As Double
    Public Property Width As Double
    Public Property Height As Double
End Class

Public NotInheritable Class SheetData
    Public Property PageNumber As Integer
    Public Property TotalPages As Integer
    Public Property Item As String = ""
    Public Property PlateNo As String = ""
    Public Property DateTimeStamp As String = ""
    Public Property DomeType As String = ""
    Public Property Capa As String = ""
    Public Property Longtail83 As String = ""
    Public Property Longtail95 As String = ""
    Public Property CoatLotNo As String = ""
    Public Property OperatorName As String = ""
    Public Property StartTrayPosition As Integer = 1
    Public Property Measurements As New List(Of Measurement)
End Class

Public NotInheritable Class SourceRecord
    Public Property CoatLotNumber As String = ""
    Public Property CoatLotSeq As Integer
    Public Property DipLotNumber As String = ""
    Public Property DipLotSeq As Integer
    Public Property RlType As String = ""
    Public Property RlpLot As String = ""
    Public Property TrayNumber As String = ""
    Public Property ItemTypeName As String = ""
    Public Property RxArrangementNumber As String = ""
    Public Property OrderRouteTypeName As String = ""
    Public Property TrayLotNumber As String = ""
    Public Property UsedFlag As String = ""
    Public Property Diameter As Double
End Class

Public NotInheritable Class ImportedReport
    Public Property Sheets As New List(Of SheetData)

    Public ReadOnly Property MeasurementCount As Integer
        Get
            Return Sheets.Sum(Function(sheet) sheet.Measurements.Count)
        End Get
    End Property
End Class

Public NotInheritable Class MySqlConnectionSettings
    Public Property Host As String = ""
    Public Property Port As UInteger = 3306UI
    Public Property DatabaseName As String = ""
    Public Property UserName As String = ""
    Public Property Password As String = ""
End Class
