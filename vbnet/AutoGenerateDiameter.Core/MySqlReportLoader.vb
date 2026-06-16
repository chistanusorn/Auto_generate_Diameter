Option Strict On
Option Explicit On

Imports MySqlConnector

Public NotInheritable Class MySqlReportLoader
    Private ReadOnly _builder As New ReportBuilder()

    Public Async Function LoadAsync(
        settings As MySqlConnectionSettings,
        productionPlaceCode As String,
        coatLotNumber As String,
        Optional cancellationToken As Threading.CancellationToken = Nothing
    ) As Task(Of ImportedReport)
        Validate(settings, productionPlaceCode, coatLotNumber)

        Dim connectionString = New MySqlConnectionStringBuilder With {
            .Server = settings.Host.Trim(),
            .Port = settings.Port,
            .Database = settings.DatabaseName.Trim(),
            .UserID = settings.UserName.Trim(),
            .Password = settings.Password,
            .CharacterSet = "utf8mb4"
        }

        Dim records As New List(Of SourceRecord)
        Using connection As New MySqlConnection(connectionString.ConnectionString)
            Await connection.OpenAsync(cancellationToken)
            Using command = connection.CreateCommand()
                command.CommandText = LoadQuery()
                command.Parameters.AddWithValue("@place_code", productionPlaceCode.Trim())
                command.Parameters.AddWithValue("@lot_number", coatLotNumber.Trim())

                Using reader = Await command.ExecuteReaderAsync(cancellationToken)
                    While Await reader.ReadAsync(cancellationToken)
                        records.Add(MapRecord(reader))
                    End While
                End Using
            End Using
        End Using

        If records.Count = 0 Then
            Throw New InvalidOperationException(
                $"No data found for PPC {productionPlaceCode} and CLN {coatLotNumber}."
            )
        End If

        Return _builder.Build(records, Date.Now.ToString("yyyy-MM-dd HH:mm"))
    End Function

    Public Shared Function LoadQuery() As String
        Dim path = IO.Path.Combine(AppContext.BaseDirectory, "coat_lot_query.sql")
        Dim query = IO.File.ReadAllText(path)
        query = ReplaceFirst(query, "%s", "@place_code")
        Return ReplaceFirst(query, "%s", "@lot_number")
    End Function

    Private Shared Function ReplaceFirst(text As String, oldValue As String, newValue As String) As String
        Dim position = text.IndexOf(oldValue, StringComparison.Ordinal)
        If position < 0 Then Throw New InvalidOperationException($"SQL placeholder {oldValue} was not found.")
        Return text.Substring(0, position) & newValue & text.Substring(position + oldValue.Length)
    End Function

    Private Shared Function MapRecord(reader As MySqlDataReader) As SourceRecord
        Return New SourceRecord With {
            .CoatLotNumber = Text(reader, "coat_lot_number"),
            .CoatLotSeq = Number(reader, "coat_lot_seq"),
            .DipLotNumber = Text(reader, "dip_lot_number"),
            .DipLotSeq = Number(reader, "diplt_seq"),
            .RlType = Text(reader, "rl_type"),
            .RlpLot = Text(reader, "rlp_lot"),
            .TrayNumber = Text(reader, "tray_number"),
            .ItemTypeName = Text(reader, "item_type_name"),
            .RxArrangementNumber = Text(reader, "rxarrangement_number"),
            .OrderRouteTypeName = Text(reader, "order_route_type_name"),
            .TrayLotNumber = Text(reader, "traylot_number"),
            .UsedFlag = Text(reader, "used_flag"),
            .Diameter = DecimalNumber(reader, "diameter")
        }
    End Function

    Private Shared Function Text(reader As MySqlDataReader, name As String) As String
        Dim value = reader(name)
        Return If(value Is DBNull.Value, "", Convert.ToString(value, Globalization.CultureInfo.InvariantCulture))
    End Function

    Private Shared Function Number(reader As MySqlDataReader, name As String) As Integer
        Return CInt(DecimalNumber(reader, name))
    End Function

    Private Shared Function DecimalNumber(reader As MySqlDataReader, name As String) As Double
        Dim value = Text(reader, name)
        Dim result As Double
        Return If(Double.TryParse(value, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, result), result, 0)
    End Function

    Private Shared Sub Validate(settings As MySqlConnectionSettings, ppc As String, cln As String)
        If settings Is Nothing Then Throw New ArgumentNullException(NameOf(settings))
        If String.IsNullOrWhiteSpace(settings.Host) OrElse
           String.IsNullOrWhiteSpace(settings.DatabaseName) OrElse
           String.IsNullOrWhiteSpace(settings.UserName) OrElse
           String.IsNullOrWhiteSpace(settings.Password) Then
            Throw New ArgumentException("MySQL Host, Database, Username and Password are required.")
        End If
        If String.IsNullOrWhiteSpace(ppc) OrElse String.IsNullOrWhiteSpace(cln) Then
            Throw New ArgumentException("PPC and CLN are required.")
        End If
    End Sub
End Class
