Option Strict On
Option Explicit On

Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

Public NotInheritable Class ManualImageAlignmentDialog
    Inherits Form

    Private ReadOnly _sourcePath As String
    Private ReadOnly _view As New CornerSelectionView()
    Private ReadOnly _instruction As New Label()
    Private ReadOnly _cropButton As New Button()
    Private ReadOnly _perspectiveButton As New Button()
    Private _preparedImagePath As String = ""
    Private _preparedGrid As DetectedGrid

    Public Property ResultImagePath As String = ""

    Public Sub New(sourcePath As String)
        _sourcePath = sourcePath
        Text = "Prepare manual image for OCR"
        StartPosition = FormStartPosition.CenterParent
        WindowState = FormWindowState.Maximized
        MinimumSize = New Size(900, 650)

        _instruction.Dock = DockStyle.Top
        _instruction.Height = 58
        _instruction.Padding = New Padding(14, 8, 14, 4)
        _instruction.Font = New Font("Segoe UI", 10, FontStyle.Bold)

        _view.Dock = DockStyle.Fill
        _view.ImagePath = sourcePath
        AddHandler _view.PointsChanged, AddressOf UpdateInstruction

        Dim resetButton As New Button With {
            .Text = "Reset corners",
            .Width = 140,
            .Height = 38
        }
        AddHandler resetButton.Click, AddressOf ResetAlignment

        Dim originalButton As New Button With {
            .Text = "Use original image",
            .Width = 170,
            .Height = 38
        }
        AddHandler originalButton.Click, AddressOf UseOriginalImage

        _cropButton.Text = "Crop frame"
        _cropButton.Width = 150
        _cropButton.Height = 38
        _cropButton.Enabled = False
        AddHandler _cropButton.Click, AddressOf CropImage

        _perspectiveButton.Text = "Perspective align"
        _perspectiveButton.Width = 170
        _perspectiveButton.Height = 38
        _perspectiveButton.Enabled = False
        AddHandler _perspectiveButton.Click, AddressOf AlignImage

        Dim cancelButton As New Button With {
            .Text = "Cancel",
            .Width = 120,
            .Height = 38,
            .DialogResult = DialogResult.Cancel
        }

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .Padding = New Padding(12, 9, 12, 7),
            .FlowDirection = FlowDirection.RightToLeft
        }
        buttons.Controls.Add(cancelButton)
        buttons.Controls.Add(_cropButton)
        buttons.Controls.Add(_perspectiveButton)
        buttons.Controls.Add(originalButton)
        buttons.Controls.Add(resetButton)

        Controls.Add(_view)
        Controls.Add(buttons)
        Controls.Add(_instruction)
        CancelButton = cancelButton
        UpdateInstruction(Me, EventArgs.Empty)
    End Sub

    Private Sub UpdateInstruction(sender As Object, eventArgs As EventArgs)
        Dim names = {"TOP-LEFT", "TOP-RIGHT", "BOTTOM-RIGHT", "BOTTOM-LEFT"}
        Dim count = _view.Points.Count
        If Not String.IsNullOrWhiteSpace(_preparedImagePath) Then
            _cropButton.Enabled = True
            _cropButton.Text = "Compare prepared image"
            _perspectiveButton.Enabled = False
            Dim gridText = If(_preparedGrid Is Nothing, "grid overlay: not detected", _preparedGrid.Summary)
            _instruction.Text = $"Preview: confirm frame and green grid overlay, then click Compare prepared image. {gridText}."
            Return
        End If
        _cropButton.Enabled = count = 4
        _cropButton.Text = "Crop frame"
        _perspectiveButton.Enabled = count = 4
        _instruction.Text =
            If(count < 4,
               $"Click the four crop-frame corners in order: {String.Join(" > ", names)}" &
               Environment.NewLine & $"Next corner: {names(count)} ({count}/4)",
               "All four corners selected. Click Crop frame to cut exactly inside the selected frame, or Perspective align to straighten skew.")
    End Sub

    Private Sub UseOriginalImage(sender As Object, eventArgs As EventArgs)
        ResultImagePath = _sourcePath
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub AlignImage(sender As Object, eventArgs As EventArgs)
        If Not String.IsNullOrWhiteSpace(_preparedImagePath) Then
            ResultImagePath = _preparedImagePath
            DialogResult = DialogResult.OK
            Close()
            Return
        End If
        If _view.Points.Count <> 4 Then Return
        Try
            Cursor = Cursors.WaitCursor
            _cropButton.Enabled = False
            _perspectiveButton.Enabled = False
            Application.DoEvents()
            _preparedImagePath = PerspectiveImageAligner.Align(_sourcePath, _view.Points, New Size(1760, 1000))
            _view.ImagePath = _preparedImagePath
            DetectPreparedGrid()
            UpdateInstruction(Me, EventArgs.Empty)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Cannot align image", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            Cursor = Cursors.Default
            _cropButton.Enabled = Not String.IsNullOrWhiteSpace(_preparedImagePath) OrElse _view.Points.Count = 4
            _perspectiveButton.Enabled = String.IsNullOrWhiteSpace(_preparedImagePath) AndAlso _view.Points.Count = 4
        End Try
    End Sub

    Private Sub CropImage(sender As Object, eventArgs As EventArgs)
        If Not String.IsNullOrWhiteSpace(_preparedImagePath) Then
            ResultImagePath = _preparedImagePath
            DialogResult = DialogResult.OK
            Close()
            Return
        End If
        If _view.Points.Count <> 4 Then Return
        Try
            Cursor = Cursors.WaitCursor
            _cropButton.Enabled = False
            _perspectiveButton.Enabled = False
            Application.DoEvents()
            _preparedImagePath = PerspectiveImageAligner.Crop(_sourcePath, _view.Points)
            _view.ImagePath = _preparedImagePath
            DetectPreparedGrid()
            UpdateInstruction(Me, EventArgs.Empty)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Cannot crop image", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            Cursor = Cursors.Default
            _cropButton.Enabled = Not String.IsNullOrWhiteSpace(_preparedImagePath) OrElse _view.Points.Count = 4
            _perspectiveButton.Enabled = String.IsNullOrWhiteSpace(_preparedImagePath) AndAlso _view.Points.Count = 4
        End Try
    End Sub

    Private Sub ResetAlignment(sender As Object, eventArgs As EventArgs)
        DeletePreparedImage()
        _view.ImagePath = _sourcePath
        _view.Grid = Nothing
        _preparedGrid = Nothing
        UpdateInstruction(Me, EventArgs.Empty)
    End Sub

    Private Sub DetectPreparedGrid()
        _preparedGrid = TableGridDetector.Detect(_preparedImagePath)
        _view.Grid = _preparedGrid
    End Sub

    Private Sub DeletePreparedImage()
        If String.IsNullOrWhiteSpace(_preparedImagePath) Then Return
        Try
            If IO.File.Exists(_preparedImagePath) Then IO.File.Delete(_preparedImagePath)
        Catch
        End Try
        _preparedImagePath = ""
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        If DialogResult <> DialogResult.OK OrElse ResultImagePath <> _preparedImagePath Then DeletePreparedImage()
        MyBase.OnFormClosed(e)
    End Sub
End Class

Public NotInheritable Class CornerSelectionView
    Inherits Control

    Private ReadOnly _points As New List(Of PointF)
    Private _image As Image
    Private _grid As DetectedGrid

    Public Event PointsChanged As EventHandler

    Public ReadOnly Property Points As IReadOnlyList(Of PointF)
        Get
            Return _points.ToList()
        End Get
    End Property

    Public Property ImagePath As String
        Set(value As String)
            If _image IsNot Nothing Then _image.Dispose()
            _image = Nothing
            If Not String.IsNullOrWhiteSpace(value) AndAlso IO.File.Exists(value) Then
                Using source = Image.FromFile(value)
                    _image = New Bitmap(source)
                End Using
            End If
            ResetPoints()
        End Set
        Get
            Return ""
        End Get
    End Property

    Public Property Grid As DetectedGrid
        Set(value As DetectedGrid)
            _grid = value
            Invalidate()
        End Set
        Get
            Return _grid
        End Get
    End Property

    Public Sub New()
        DoubleBuffered = True
        BackColor = Color.FromArgb(36, 40, 46)
        Cursor = Cursors.Cross
    End Sub

    Public Sub ResetPoints()
        _points.Clear()
        Invalidate()
        RaiseEvent PointsChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button <> MouseButtons.Left OrElse _image Is Nothing OrElse _points.Count >= 4 Then Return
        Dim imageBounds = DisplayBounds()
        If Not imageBounds.Contains(CSng(e.X), CSng(e.Y)) Then Return

        Dim imageX = CSng((e.X - imageBounds.X) * _image.Width / imageBounds.Width)
        Dim imageY = CSng((e.Y - imageBounds.Y) * _image.Height / imageBounds.Height)
        _points.Add(New PointF(imageX, imageY))
        Invalidate()
        RaiseEvent PointsChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        e.Graphics.Clear(BackColor)
        If _image Is Nothing Then Return

        Dim bounds = DisplayBounds()
        e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
        e.Graphics.DrawImage(_image, bounds)
        DrawGridOverlay(e.Graphics, bounds)

        Dim displayPoints = _points.Select(Function(point) ImageToDisplay(point, bounds)).ToArray()
        If displayPoints.Length > 1 Then
            Using linePen As New Pen(Color.Red, 3)
                e.Graphics.DrawLines(linePen, displayPoints)
                If displayPoints.Length = 4 Then e.Graphics.DrawLine(linePen, displayPoints(3), displayPoints(0))
            End Using
        End If

        For index = 0 To displayPoints.Length - 1
            Dim point = displayPoints(index)
            Using brush As New SolidBrush(Color.Red)
                e.Graphics.FillEllipse(brush, point.X - 9, point.Y - 9, 18, 18)
            End Using
            Using font As New Font("Segoe UI", 10, FontStyle.Bold),
                  brush As New SolidBrush(Color.White)
                e.Graphics.DrawString((index + 1).ToString(), font, brush, point.X + 10, point.Y - 20)
            End Using
        Next
    End Sub

    Private Sub DrawGridOverlay(graphics As Graphics, bounds As RectangleF)
        If _grid Is Nothing OrElse _image Is Nothing Then Return
        Using linePen As New Pen(Color.FromArgb(210, 0, 210, 80), 2)
            If _grid.VerticalPaths.Count > 0 OrElse _grid.HorizontalPaths.Count > 0 Then
                For Each path In _grid.VerticalPaths
                    DrawGridPath(graphics, linePen, path, bounds)
                Next
                For Each path In _grid.HorizontalPaths
                    DrawGridPath(graphics, linePen, path, bounds)
                Next
            ElseIf _grid.VerticalSegments.Count > 0 OrElse _grid.HorizontalSegments.Count > 0 Then
                For Each segment In _grid.VerticalSegments
                    graphics.DrawLine(linePen, ImageToDisplay(New PointF(segment.X1, segment.Y1), bounds), ImageToDisplay(New PointF(segment.X2, segment.Y2), bounds))
                Next
                For Each segment In _grid.HorizontalSegments
                    graphics.DrawLine(linePen, ImageToDisplay(New PointF(segment.X1, segment.Y1), bounds), ImageToDisplay(New PointF(segment.X2, segment.Y2), bounds))
                Next
            Else
                For Each imageX In _grid.VerticalLines
                    Dim displayX = bounds.X + imageX * bounds.Width / _image.Width
                    graphics.DrawLine(linePen, displayX, bounds.Top, displayX, bounds.Bottom)
                Next
                For Each imageY In _grid.HorizontalLines
                    Dim displayY = bounds.Y + imageY * bounds.Height / _image.Height
                    graphics.DrawLine(linePen, bounds.Left, displayY, bounds.Right, displayY)
                Next
            End If
        End Using
    End Sub

    Private Sub DrawGridPath(graphics As Graphics, pen As Pen, path As GridLinePath, bounds As RectangleF)
        If path Is Nothing OrElse path.Points.Count = 0 Then Return
        If path.Points.Count = 1 Then
            Dim point = ImageToDisplay(path.Points(0), bounds)
            graphics.DrawEllipse(pen, point.X - 1.5F, point.Y - 1.5F, 3, 3)
            Return
        End If

        Dim displayPoints = path.Points.Select(Function(point) ImageToDisplay(point, bounds)).ToArray()
        graphics.DrawLines(pen, displayPoints)
    End Sub

    Private Function DisplayBounds() As RectangleF
        If _image Is Nothing OrElse ClientSize.Width <= 0 OrElse ClientSize.Height <= 0 Then Return RectangleF.Empty
        Dim scale = Math.Min(ClientSize.Width / CSng(_image.Width), ClientSize.Height / CSng(_image.Height))
        Dim width = _image.Width * scale
        Dim height = _image.Height * scale
        Return New RectangleF((ClientSize.Width - width) / 2.0F, (ClientSize.Height - height) / 2.0F, width, height)
    End Function

    Private Function ImageToDisplay(point As PointF, bounds As RectangleF) As PointF
        Return New PointF(
            bounds.X + point.X * bounds.Width / _image.Width,
            bounds.Y + point.Y * bounds.Height / _image.Height)
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso _image IsNot Nothing Then _image.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class

Public NotInheritable Class PerspectiveImageAligner
    Private Sub New()
    End Sub

    Public Shared Function Align(sourcePath As String, corners As IReadOnlyList(Of PointF), outputSize As Size) As String
        If corners Is Nothing OrElse corners.Count <> 4 Then Throw New ArgumentException("Select all four paper corners.")
        If outputSize.Width <= 0 OrElse outputSize.Height <= 0 Then Throw New ArgumentException("Invalid output image size.")

        Using source As New Bitmap(sourcePath)
            ValidateCorners(corners, source.Size)
            Dim destination = {
                New PointF(0, 0),
                New PointF(outputSize.Width - 1, 0),
                New PointF(outputSize.Width - 1, outputSize.Height - 1),
                New PointF(0, outputSize.Height - 1)
            }
            Dim transform = SolveHomography(destination, corners)
            Dim outputPath = IO.Path.Combine(IO.Path.GetTempPath(), $"AutoGenerateDiameter-aligned-{Guid.NewGuid():N}.png")
            Using output = WarpBitmap(source, outputSize, transform)
                output.Save(outputPath, ImageFormat.Png)
            End Using
            Return outputPath
        End Using
    End Function

    Public Shared Function Crop(sourcePath As String, corners As IReadOnlyList(Of PointF)) As String
        If corners Is Nothing OrElse corners.Count <> 4 Then Throw New ArgumentException("Select all four crop-frame corners.")

        Using source As New Bitmap(sourcePath)
            ValidateCorners(corners, source.Size)
            Dim left = Math.Max(0, CInt(Math.Floor(corners.Min(Function(point) point.X))))
            Dim top = Math.Max(0, CInt(Math.Floor(corners.Min(Function(point) point.Y))))
            Dim right = Math.Min(source.Width, CInt(Math.Ceiling(corners.Max(Function(point) point.X))))
            Dim bottom = Math.Min(source.Height, CInt(Math.Ceiling(corners.Max(Function(point) point.Y))))
            If right <= left OrElse bottom <= top Then Throw New InvalidOperationException("The selected crop frame is invalid.")

            Dim cropRect As New Rectangle(left, top, right - left, bottom - top)
            Using output As New Bitmap(cropRect.Width, cropRect.Height, PixelFormat.Format24bppRgb)
                Using cropGraphics = Graphics.FromImage(output)
                    cropGraphics.DrawImage(source, New Rectangle(0, 0, output.Width, output.Height), cropRect, GraphicsUnit.Pixel)
                End Using
                Dim outputPath = IO.Path.Combine(IO.Path.GetTempPath(), $"AutoGenerateDiameter-aligned-{Guid.NewGuid():N}.png")
                output.Save(outputPath, ImageFormat.Png)
                Return outputPath
            End Using
        End Using
    End Function

    Private Shared Sub ValidateCorners(corners As IReadOnlyList(Of PointF), imageSize As Size)
        Dim signedArea As Double = 0
        For index = 0 To 3
            Dim current = corners(index)
            Dim following = corners((index + 1) Mod 4)
            If current.X < 0 OrElse current.X >= imageSize.Width OrElse current.Y < 0 OrElse current.Y >= imageSize.Height Then
                Throw New InvalidOperationException("A selected corner is outside the image.")
            End If
            signedArea += current.X * following.Y - following.X * current.Y
        Next
        Dim area = Math.Abs(signedArea) / 2.0
        If area < imageSize.Width * imageSize.Height * 0.05 Then
            Throw New InvalidOperationException("The selected table area is too small. Reset and select the four outer table corners.")
        End If
        If signedArea < 0 Then
            Throw New InvalidOperationException("Corner order is incorrect. Select TOP-LEFT, TOP-RIGHT, BOTTOM-RIGHT, then BOTTOM-LEFT.")
        End If
    End Sub

    Private Shared Function WarpBitmap(source As Bitmap, outputSize As Size, transform As Double()) As Bitmap
        Using readableSource As New Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb)
            Using imageGraphics = Graphics.FromImage(readableSource)
                imageGraphics.DrawImageUnscaled(source, 0, 0)
            End Using

            Dim output As New Bitmap(outputSize.Width, outputSize.Height, PixelFormat.Format24bppRgb)
            Dim sourceData = readableSource.LockBits(
                New Rectangle(0, 0, readableSource.Width, readableSource.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb)
            Dim outputData = output.LockBits(
                New Rectangle(0, 0, output.Width, output.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb)
            Try
                Dim sourceBytes(Math.Abs(sourceData.Stride) * readableSource.Height - 1) As Byte
                Dim outputBytes(Math.Abs(outputData.Stride) * output.Height - 1) As Byte
                Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length)
                Array.Fill(outputBytes, CByte(255))

                For y = 0 To output.Height - 1
                    For x = 0 To output.Width - 1
                        Dim divisor = transform(6) * x + transform(7) * y + 1.0
                        If Math.Abs(divisor) < 0.000001 Then Continue For
                        Dim sourceX = CInt(Math.Floor((transform(0) * x + transform(1) * y + transform(2)) / divisor))
                        Dim sourceY = CInt(Math.Floor((transform(3) * x + transform(4) * y + transform(5)) / divisor))
                        If sourceX < 0 OrElse sourceX >= readableSource.Width OrElse sourceY < 0 OrElse sourceY >= readableSource.Height Then Continue For

                        Dim sourceOffset = sourceY * sourceData.Stride + sourceX * 3
                        Dim outputOffset = y * outputData.Stride + x * 3
                        outputBytes(outputOffset) = sourceBytes(sourceOffset)
                        outputBytes(outputOffset + 1) = sourceBytes(sourceOffset + 1)
                        outputBytes(outputOffset + 2) = sourceBytes(sourceOffset + 2)
                    Next
                Next
                Marshal.Copy(outputBytes, 0, outputData.Scan0, outputBytes.Length)
            Finally
                readableSource.UnlockBits(sourceData)
                output.UnlockBits(outputData)
            End Try
            Return output
        End Using
    End Function

    Private Shared Function SolveHomography(fromPoints As IReadOnlyList(Of PointF), toPoints As IReadOnlyList(Of PointF)) As Double()
        Dim matrix(7, 8) As Double
        For index = 0 To 3
            Dim x = CDbl(fromPoints(index).X)
            Dim y = CDbl(fromPoints(index).Y)
            Dim u = CDbl(toPoints(index).X)
            Dim v = CDbl(toPoints(index).Y)
            Dim firstRow = index * 2
            Dim secondRow = firstRow + 1

            matrix(firstRow, 0) = x
            matrix(firstRow, 1) = y
            matrix(firstRow, 2) = 1
            matrix(firstRow, 6) = -u * x
            matrix(firstRow, 7) = -u * y
            matrix(firstRow, 8) = u

            matrix(secondRow, 3) = x
            matrix(secondRow, 4) = y
            matrix(secondRow, 5) = 1
            matrix(secondRow, 6) = -v * x
            matrix(secondRow, 7) = -v * y
            matrix(secondRow, 8) = v
        Next

        For pivot = 0 To 7
            Dim bestRow = pivot
            For row = pivot + 1 To 7
                If Math.Abs(matrix(row, pivot)) > Math.Abs(matrix(bestRow, pivot)) Then bestRow = row
            Next
            If Math.Abs(matrix(bestRow, pivot)) < 0.000000001 Then Throw New InvalidOperationException("The selected corners cannot form a valid page.")
            If bestRow <> pivot Then
                For column = pivot To 8
                    Dim temporary = matrix(pivot, column)
                    matrix(pivot, column) = matrix(bestRow, column)
                    matrix(bestRow, column) = temporary
                Next
            End If

            Dim divisor = matrix(pivot, pivot)
            For column = pivot To 8
                matrix(pivot, column) /= divisor
            Next
            For row = 0 To 7
                If row = pivot Then Continue For
                Dim factor = matrix(row, pivot)
                For column = pivot To 8
                    matrix(row, column) -= factor * matrix(pivot, column)
                Next
            Next
        Next

        Dim result(7) As Double
        For index = 0 To 7
            result(index) = matrix(index, 8)
        Next
        Return result
    End Function
End Class
