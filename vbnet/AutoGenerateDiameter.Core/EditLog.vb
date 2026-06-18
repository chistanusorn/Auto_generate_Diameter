Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Text
Imports ClosedXML.Excel

' One audit row: a single field that an operator changed, with the value before and
' after the edit and when it happened. Cell, tray, header and operator edits all map
' onto this same shape so the history reads uniformly.
Public NotInheritable Class EditLogEntry
    Public Property Timestamp As DateTime
    Public Property OperatorName As String = ""
    Public Property Target As String = ""
    Public Property Field As String = ""
    Public Property OldValue As String = ""
    Public Property NewValue As String = ""
End Class

' Appends edit history to a CSV file (kept across sessions) and reads it back for the
' in-app viewer. Can also render the same history to an .xlsx workbook for printing.
Public NotInheritable Class EditLogStore
    Private Const TimestampFormat As String = "yyyy-MM-dd HH:mm:ss"
    Private Shared ReadOnly Columns As String() = {"Timestamp", "Operator", "Target", "Field", "Before", "After"}

    Private ReadOnly _path As String

    Public Sub New(path As String)
        If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException("Log path is required.", NameOf(path))
        _path = path
    End Sub

    Public ReadOnly Property FilePath As String
        Get
            Return _path
        End Get
    End Property

    Public Sub Append(entries As IEnumerable(Of EditLogEntry))
        Dim rows = If(entries Is Nothing, New List(Of EditLogEntry), entries.ToList())
        If rows.Count = 0 Then Return

        Dim directory = IO.Path.GetDirectoryName(_path)
        If Not String.IsNullOrEmpty(directory) Then IO.Directory.CreateDirectory(directory)

        Dim isNew = Not IO.File.Exists(_path)
        Using writer As New IO.StreamWriter(_path, True, New UTF8Encoding(True))
            If isNew Then writer.WriteLine(String.Join(",", Columns.Select(AddressOf Quote)))
            For Each entry In rows
                writer.WriteLine(String.Join(",", Fields(entry).Select(AddressOf Quote)))
            Next
        End Using
    End Sub

    Public Function ReadAll() As List(Of EditLogEntry)
        Dim entries As New List(Of EditLogEntry)
        If Not IO.File.Exists(_path) Then Return entries

        Dim lines = IO.File.ReadAllLines(_path, Encoding.UTF8)
        For index = 1 To lines.Length - 1
            If String.IsNullOrWhiteSpace(lines(index)) Then Continue For
            Dim fields = ParseCsvLine(lines(index))
            If fields.Count < Columns.Length Then Continue For
            Dim stamp As DateTime
            DateTime.TryParseExact(fields(0), TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, stamp)
            entries.Add(New EditLogEntry With {
                .Timestamp = stamp,
                .OperatorName = fields(1),
                .Target = fields(2),
                .Field = fields(3),
                .OldValue = fields(4),
                .NewValue = fields(5)
            })
        Next
        Return entries
    End Function

    Public Sub ExportToXlsx(path As String)
        ExportToXlsx(path, ReadAll())
    End Sub

    Public Sub ExportToXlsx(path As String, entries As IEnumerable(Of EditLogEntry))
        Dim rows = If(entries Is Nothing, New List(Of EditLogEntry), entries.ToList())
        Using workbook As New XLWorkbook()
            Dim worksheet = workbook.Worksheets.Add("Edit Log")
            For column = 0 To Columns.Length - 1
                worksheet.Cell(1, column + 1).Value = Columns(column)
            Next
            worksheet.Row(1).Style.Font.Bold = True

            Dim rowNumber = 2
            For Each entry In rows
                Dim values = Fields(entry)
                For column = 0 To values.Length - 1
                    worksheet.Cell(rowNumber, column + 1).Value = values(column)
                Next
                rowNumber += 1
            Next

            worksheet.Columns().AdjustToContents()
            workbook.SaveAs(path)
        End Using
    End Sub

    Private Shared Function Fields(entry As EditLogEntry) As String()
        Return {
            entry.Timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture),
            entry.OperatorName,
            entry.Target,
            entry.Field,
            entry.OldValue,
            entry.NewValue
        }
    End Function

    Private Shared Function Quote(value As String) As String
        Dim text = If(value, "")
        Return $"""{text.Replace("""", """""")}"""
    End Function

    Private Shared Function ParseCsvLine(line As String) As List(Of String)
        Dim fields As New List(Of String)
        Dim builder As New StringBuilder()
        Dim inQuotes = False
        Dim index = 0
        While index < line.Length
            Dim ch = line(index)
            If inQuotes Then
                If ch = """"c Then
                    If index + 1 < line.Length AndAlso line(index + 1) = """"c Then
                        builder.Append(""""c)
                        index += 1
                    Else
                        inQuotes = False
                    End If
                Else
                    builder.Append(ch)
                End If
            ElseIf ch = """"c Then
                inQuotes = True
            ElseIf ch = ","c Then
                fields.Add(builder.ToString())
                builder.Clear()
            Else
                builder.Append(ch)
            End If
            index += 1
        End While
        fields.Add(builder.ToString())
        Return fields
    End Function
End Class
