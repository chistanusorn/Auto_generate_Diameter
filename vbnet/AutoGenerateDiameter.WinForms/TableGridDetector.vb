Option Strict On
Option Explicit On

Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

Public NotInheritable Class GridLineSegment
    Public Property X1 As Single
    Public Property Y1 As Single
    Public Property X2 As Single
    Public Property Y2 As Single
End Class

Public NotInheritable Class GridLinePath
    Public Property Points As New List(Of PointF)
End Class

Public NotInheritable Class GridCell
    Public Property ColumnIndex As Integer
    Public Property RowIndex As Integer
    Public Property Corners As New List(Of PointF)
End Class

Public NotInheritable Class DetectedGrid
    Public Property VerticalLines As New List(Of Single)
    Public Property HorizontalLines As New List(Of Single)
    Public Property VerticalSegments As New List(Of GridLineSegment)
    Public Property HorizontalSegments As New List(Of GridLineSegment)
    Public Property VerticalPaths As New List(Of GridLinePath)
    Public Property HorizontalPaths As New List(Of GridLinePath)
    Public Property Cells As New List(Of GridCell)

    Public ReadOnly Property Summary As String
        Get
            Dim verticalCount = If(VerticalPaths.Count > 0, VerticalPaths.Count, If(VerticalSegments.Count > 0, VerticalSegments.Count, VerticalLines.Count))
            Dim horizontalCount = If(HorizontalPaths.Count > 0, HorizontalPaths.Count, If(HorizontalSegments.Count > 0, HorizontalSegments.Count, HorizontalLines.Count))
            Return $"grid overlay: {verticalCount} vertical, {horizontalCount} horizontal, {Cells.Count} cells"
        End Get
    End Property
End Class

Public NotInheritable Class TableGridDetector
    Private Sub New()
    End Sub

    Public Shared Function Detect(imagePath As String) As DetectedGrid
        If String.IsNullOrWhiteSpace(imagePath) OrElse Not IO.File.Exists(imagePath) Then Return New DetectedGrid()

        Using source As New Bitmap(imagePath)
            Using bitmap As New Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb)
                Using imageGraphics = Graphics.FromImage(bitmap)
                    imageGraphics.DrawImageUnscaled(source, 0, 0)
                End Using
                Dim verticalScores(bitmap.Width - 1) As Integer
                Dim horizontalScores(bitmap.Height - 1) As Integer
                Dim gridMask(bitmap.Width * bitmap.Height - 1) As Boolean
                ScoreDarkGridPixels(bitmap, verticalScores, horizontalScores, gridMask)
                Dim verticalLines = MergeCloseLineCenters(
                    FindLineCenters(Smooth(verticalScores, 2), bitmap.Height, Math.Max(4, bitmap.Width \ 220)),
                    Math.Max(4.0F, bitmap.Width / 260.0F))
                Dim horizontalLines = MergeCloseLineCenters(
                    FindLineCenters(Smooth(horizontalScores, 2), bitmap.Width, Math.Max(3, bitmap.Height \ 180)),
                    Math.Max(5.0F, bitmap.Height / 120.0F))
                Dim verticalPaths = FitVerticalPaths(gridMask, bitmap.Width, bitmap.Height, verticalLines)
                Dim horizontalPaths = RemoveDuplicateHorizontalPaths(FitHorizontalPaths(gridMask, bitmap.Width, bitmap.Height, horizontalLines))
                Dim cells = BuildCellMap(verticalPaths, horizontalPaths, bitmap.Width, bitmap.Height)

                Return New DetectedGrid With {
                    .VerticalLines = verticalLines,
                    .HorizontalLines = horizontalLines,
                    .VerticalPaths = verticalPaths,
                    .HorizontalPaths = horizontalPaths,
                    .VerticalSegments = PathsToSegments(verticalPaths),
                    .HorizontalSegments = PathsToSegments(horizontalPaths),
                    .Cells = cells
                }
            End Using
        End Using
    End Function

    Private Shared Sub ScoreDarkGridPixels(bitmap As Bitmap, verticalScores As Integer(), horizontalScores As Integer(), gridMask As Boolean())
        Dim data = bitmap.LockBits(New Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb)
        Try
            Dim bytes(Math.Abs(data.Stride) * bitmap.Height - 1) As Byte
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length)
            For y = 0 To bitmap.Height - 1
                Dim rowOffset = y * data.Stride
                For x = 0 To bitmap.Width - 1
                    Dim offset = rowOffset + x * 3
                    Dim b = CInt(bytes(offset))
                    Dim g = CInt(bytes(offset + 1))
                    Dim r = CInt(bytes(offset + 2))
                    If IsLikelyGridPixel(r, g, b) Then
                        verticalScores(x) += 1
                        horizontalScores(y) += 1
                        gridMask(y * bitmap.Width + x) = True
                    End If
                Next
            Next
        Finally
            bitmap.UnlockBits(data)
        End Try
    End Sub

    Private Shared Function IsLikelyGridPixel(r As Integer, g As Integer, b As Integer) As Boolean
        Dim luminance = (r * 299 + g * 587 + b * 114) \ 1000
        Dim colorSpread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))

        ' Grid lines are normally gray/black. This avoids counting blue handwriting as table lines.
        If luminance < 115 AndAlso colorSpread < 75 Then Return True
        Return luminance < 145 AndAlso colorSpread < 38
    End Function

    Private Shared Function Smooth(values As Integer(), radius As Integer) As Integer()
        Dim result(values.Length - 1) As Integer
        For index = 0 To values.Length - 1
            Dim total = 0
            Dim count = 0
            For offset = -radius To radius
                Dim cursor = index + offset
                If cursor < 0 OrElse cursor >= values.Length Then Continue For
                total += values(cursor)
                count += 1
            Next
            result(index) = If(count = 0, values(index), total \ count)
        Next
        Return result
    End Function

    Private Shared Function FindLineCenters(scores As Integer(), perpendicularLength As Integer, minGap As Integer) As List(Of Single)
        Dim result As New List(Of Single)
        If scores.Length = 0 Then Return result

        Dim maxScore = scores.Max()
        If maxScore <= 0 Then Return result
        Dim threshold = Math.Max(CInt(perpendicularLength * 0.08), CInt(maxScore * 0.34))

        Dim index = 0
        While index < scores.Length
            If scores(index) < threshold Then
                index += 1
                Continue While
            End If

            Dim weightedTotal As Long = 0
            Dim weight As Long = 0
            Dim peakScore = scores(index)
            While index < scores.Length AndAlso scores(index) >= threshold
                weightedTotal += CLng(index) * scores(index)
                weight += scores(index)
                peakScore = Math.Max(peakScore, scores(index))
                index += 1
            End While

            If weight > 0 AndAlso peakScore >= threshold Then
                Dim center = CSng(weightedTotal / CDbl(weight))
                If result.Count = 0 OrElse center - result(result.Count - 1) >= minGap Then
                    result.Add(center)
                Else
                    result(result.Count - 1) = (result(result.Count - 1) + center) / 2.0F
                End If
            End If
        End While

        Return result
    End Function

    Private Shared Function MergeCloseLineCenters(centers As IReadOnlyList(Of Single), minimumDistance As Single) As List(Of Single)
        Dim result As New List(Of Single)
        If centers Is Nothing OrElse centers.Count = 0 Then Return result

        Dim sorted = centers.OrderBy(Function(center) center).ToList()
        Dim clusterTotal = sorted(0)
        Dim clusterCount = 1
        Dim previous = sorted(0)

        For index = 1 To sorted.Count - 1
            Dim current = sorted(index)
            If current - previous <= minimumDistance Then
                clusterTotal += current
                clusterCount += 1
            Else
                result.Add(clusterTotal / clusterCount)
                clusterTotal = current
                clusterCount = 1
            End If
            previous = current
        Next

        result.Add(clusterTotal / clusterCount)
        Return result
    End Function

    Private Shared Function FitHorizontalPaths(gridMask As Boolean(), width As Integer, height As Integer, centers As IReadOnlyList(Of Single)) As List(Of GridLinePath)
        Dim result As New List(Of GridLinePath)
        Dim searchRadius = Math.Max(5, height \ 70)
        Dim blockWidth = Math.Max(60, width \ 18)
        Dim sampleStep = Math.Max(2, width \ 450)

        For Each center In centers
            Dim path As New GridLinePath()
            For blockStart = 0 To width - 1 Step blockWidth
                Dim blockEnd = Math.Min(width - 1, blockStart + blockWidth)
                Dim points As New List(Of PointF)
                Dim centerY = CInt(Math.Round(center))
                For x As Integer = blockStart To blockEnd Step sampleStep
                    Dim bestY = -1
                    Dim bestScore = 0
                    Dim top = Math.Max(0, centerY - searchRadius)
                    Dim bottom = Math.Min(height - 1, centerY + searchRadius)
                    For y As Integer = top To bottom
                        Dim score = CountHorizontalSupport(gridMask, width, height, x, y)
                        If score > bestScore Then
                            bestScore = score
                            bestY = y
                        End If
                    Next
                    If bestY >= 0 AndAlso bestScore >= 3 Then points.Add(New PointF(x, bestY))
                Next

                Dim xAtBlock = CSng((blockStart + blockEnd) / 2.0F)
                Dim yAtBlock = FitLocalY(points, xAtBlock, center)
                path.Points.Add(New PointF(xAtBlock, Clamp(yAtBlock, 0, height - 1)))
            Next

            EnsureHorizontalPathEdges(path, width, height, center)
            result.Add(SmoothPath(path, True))
        Next

        Return result
    End Function

    Private Shared Function FitVerticalPaths(gridMask As Boolean(), width As Integer, height As Integer, centers As IReadOnlyList(Of Single)) As List(Of GridLinePath)
        Dim result As New List(Of GridLinePath)
        Dim searchRadius = Math.Max(5, width \ 120)
        Dim blockHeight = Math.Max(45, height \ 16)
        Dim sampleStep = Math.Max(2, height \ 350)

        For Each center In centers
            Dim path As New GridLinePath()
            For blockStart = 0 To height - 1 Step blockHeight
                Dim blockEnd = Math.Min(height - 1, blockStart + blockHeight)
                Dim points As New List(Of PointF)
                Dim centerX = CInt(Math.Round(center))
                For y As Integer = blockStart To blockEnd Step sampleStep
                    Dim bestX = -1
                    Dim bestScore = 0
                    Dim left = Math.Max(0, centerX - searchRadius)
                    Dim right = Math.Min(width - 1, centerX + searchRadius)
                    For x As Integer = left To right
                        Dim score = CountVerticalSupport(gridMask, width, height, x, y)
                        If score > bestScore Then
                            bestScore = score
                            bestX = x
                        End If
                    Next
                    If bestX >= 0 AndAlso bestScore >= 3 Then points.Add(New PointF(bestX, y))
                Next

                Dim yAtBlock = CSng((blockStart + blockEnd) / 2.0F)
                Dim xAtBlock = FitLocalX(points, yAtBlock, center)
                path.Points.Add(New PointF(Clamp(xAtBlock, 0, width - 1), yAtBlock))
            Next

            EnsureVerticalPathEdges(path, width, height, center)
            result.Add(SmoothPath(path, False))
        Next

        Return result
    End Function

    Private Shared Function RemoveDuplicateHorizontalPaths(paths As IReadOnlyList(Of GridLinePath)) As List(Of GridLinePath)
        Dim result As New List(Of GridLinePath)
        If paths Is Nothing OrElse paths.Count = 0 Then Return result

        Dim sorted = paths.OrderBy(Function(path) AveragePathY(path)).ToList()
        If sorted.Count <= 2 Then Return sorted

        Dim gaps As New List(Of Single)
        For index = 1 To sorted.Count - 1
            Dim gap = AveragePathY(sorted(index)) - AveragePathY(sorted(index - 1))
            If gap > 0.1F Then gaps.Add(gap)
        Next
        If gaps.Count = 0 Then Return sorted

        gaps.Sort()
        Dim medianGap = gaps(gaps.Count \ 2)
        Dim duplicateDistance = Math.Max(6.0F, medianGap * 0.45F)

        For Each path In sorted
            If result.Count = 0 Then
                result.Add(path)
                Continue For
            End If

            Dim previous = result(result.Count - 1)
            If AveragePathY(path) - AveragePathY(previous) <= duplicateDistance Then
                result(result.Count - 1) = MergePaths(previous, path)
            Else
                result.Add(path)
            End If
        Next

        Return result
    End Function

    Private Shared Function PathsToSegments(paths As IReadOnlyList(Of GridLinePath)) As List(Of GridLineSegment)
        Dim result As New List(Of GridLineSegment)
        If paths Is Nothing Then Return result

        For Each path In paths
            If path.Points.Count = 0 Then Continue For
            Dim first = path.Points(0)
            Dim last = path.Points(path.Points.Count - 1)
            result.Add(New GridLineSegment With {.X1 = first.X, .Y1 = first.Y, .X2 = last.X, .Y2 = last.Y})
        Next

        Return result
    End Function

    Private Shared Function BuildCellMap(verticalPaths As IReadOnlyList(Of GridLinePath), horizontalPaths As IReadOnlyList(Of GridLinePath), width As Integer, height As Integer) As List(Of GridCell)
        Dim result As New List(Of GridCell)
        If verticalPaths Is Nothing OrElse horizontalPaths Is Nothing Then Return result
        If verticalPaths.Count < 2 OrElse horizontalPaths.Count < 2 Then Return result

        Dim verticals = verticalPaths.Where(Function(path) path.Points.Count >= 2).OrderBy(Function(path) AveragePathX(path)).ToList()
        Dim horizontals = horizontalPaths.Where(Function(path) path.Points.Count >= 2).OrderBy(Function(path) AveragePathY(path)).ToList()
        If verticals.Count < 2 OrElse horizontals.Count < 2 Then Return result

        For row = 0 To horizontals.Count - 2
            For column = 0 To verticals.Count - 2
                Dim topLeft = IntersectPaths(verticals(column), horizontals(row), width, height)
                Dim topRight = IntersectPaths(verticals(column + 1), horizontals(row), width, height)
                Dim bottomRight = IntersectPaths(verticals(column + 1), horizontals(row + 1), width, height)
                Dim bottomLeft = IntersectPaths(verticals(column), horizontals(row + 1), width, height)
                If IsReasonableCell(topLeft, topRight, bottomRight, bottomLeft) Then
                    result.Add(New GridCell With {
                        .ColumnIndex = column,
                        .RowIndex = row,
                        .Corners = New List(Of PointF) From {topLeft, topRight, bottomRight, bottomLeft}
                    })
                End If
            Next
        Next

        Return result
    End Function

    Private Shared Function IntersectPaths(verticalPath As GridLinePath, horizontalPath As GridLinePath, width As Integer, height As Integer) As PointF
        Dim x = AveragePathX(verticalPath)
        Dim y = EvaluateHorizontalPath(horizontalPath, x)

        For iteration = 0 To 5
            x = EvaluateVerticalPath(verticalPath, y)
            y = EvaluateHorizontalPath(horizontalPath, x)
        Next

        Return New PointF(Clamp(x, 0, width - 1), Clamp(y, 0, height - 1))
    End Function

    Private Shared Function EvaluateHorizontalPath(path As GridLinePath, x As Single) As Single
        Dim points = path.Points.OrderBy(Function(point) point.X).ToList()
        If points.Count = 0 Then Return 0
        If points.Count = 1 OrElse x <= points(0).X Then Return points(0).Y
        If x >= points(points.Count - 1).X Then Return points(points.Count - 1).Y

        For index = 1 To points.Count - 1
            If x > points(index).X Then Continue For
            Dim left = points(index - 1)
            Dim right = points(index)
            Dim span = right.X - left.X
            If Math.Abs(span) < 0.0001F Then Return right.Y
            Dim ratio = (x - left.X) / span
            Return left.Y + (right.Y - left.Y) * ratio
        Next

        Return points(points.Count - 1).Y
    End Function

    Private Shared Function EvaluateVerticalPath(path As GridLinePath, y As Single) As Single
        Dim points = path.Points.OrderBy(Function(point) point.Y).ToList()
        If points.Count = 0 Then Return 0
        If points.Count = 1 OrElse y <= points(0).Y Then Return points(0).X
        If y >= points(points.Count - 1).Y Then Return points(points.Count - 1).X

        For index = 1 To points.Count - 1
            If y > points(index).Y Then Continue For
            Dim top = points(index - 1)
            Dim bottom = points(index)
            Dim span = bottom.Y - top.Y
            If Math.Abs(span) < 0.0001F Then Return bottom.X
            Dim ratio = (y - top.Y) / span
            Return top.X + (bottom.X - top.X) * ratio
        Next

        Return points(points.Count - 1).X
    End Function

    Private Shared Function IsReasonableCell(topLeft As PointF, topRight As PointF, bottomRight As PointF, bottomLeft As PointF) As Boolean
        Dim topWidth = Distance(topLeft, topRight)
        Dim bottomWidth = Distance(bottomLeft, bottomRight)
        Dim leftHeight = Distance(topLeft, bottomLeft)
        Dim rightHeight = Distance(topRight, bottomRight)
        If topWidth < 2 OrElse bottomWidth < 2 OrElse leftHeight < 2 OrElse rightHeight < 2 Then Return False
        Return PolygonArea(topLeft, topRight, bottomRight, bottomLeft) >= 4
    End Function

    Private Shared Function Distance(first As PointF, second As PointF) As Single
        Dim dx = first.X - second.X
        Dim dy = first.Y - second.Y
        Return CSng(Math.Sqrt(dx * dx + dy * dy))
    End Function

    Private Shared Function PolygonArea(topLeft As PointF, topRight As PointF, bottomRight As PointF, bottomLeft As PointF) As Single
        Dim points = {topLeft, topRight, bottomRight, bottomLeft}
        Dim area As Double = 0
        For index = 0 To points.Length - 1
            Dim current = points(index)
            Dim following = points((index + 1) Mod points.Length)
            area += current.X * following.Y - following.X * current.Y
        Next
        Return CSng(Math.Abs(area) / 2.0)
    End Function

    Private Shared Function FitLocalY(points As IReadOnlyList(Of PointF), targetX As Single, fallbackY As Single) As Single
        If points.Count = 0 Then Return fallbackY
        If points.Count < 4 Then Return CSng(points.Average(Function(point) point.Y))

        Dim sumX As Double = 0
        Dim sumY As Double = 0
        Dim sumXX As Double = 0
        Dim sumXY As Double = 0
        For Each point In points
            sumX += point.X
            sumY += point.Y
            sumXX += point.X * point.X
            sumXY += point.X * point.Y
        Next

        Dim count = CDbl(points.Count)
        Dim denominator = count * sumXX - sumX * sumX
        If Math.Abs(denominator) < 0.000001 Then Return CSng(sumY / count)

        Dim slope = (count * sumXY - sumX * sumY) / denominator
        If Math.Abs(slope) > 0.35 Then Return CSng(sumY / count)
        Dim intercept = (sumY - slope * sumX) / count
        Return CSng(slope * targetX + intercept)
    End Function

    Private Shared Function FitLocalX(points As IReadOnlyList(Of PointF), targetY As Single, fallbackX As Single) As Single
        If points.Count = 0 Then Return fallbackX
        If points.Count < 4 Then Return CSng(points.Average(Function(point) point.X))

        Dim sumX As Double = 0
        Dim sumY As Double = 0
        Dim sumYY As Double = 0
        Dim sumXY As Double = 0
        For Each point In points
            sumX += point.X
            sumY += point.Y
            sumYY += point.Y * point.Y
            sumXY += point.X * point.Y
        Next

        Dim count = CDbl(points.Count)
        Dim denominator = count * sumYY - sumY * sumY
        If Math.Abs(denominator) < 0.000001 Then Return CSng(sumX / count)

        Dim slope = (count * sumXY - sumX * sumY) / denominator
        If Math.Abs(slope) > 0.35 Then Return CSng(sumX / count)
        Dim intercept = (sumX - slope * sumY) / count
        Return CSng(slope * targetY + intercept)
    End Function

    Private Shared Sub EnsureHorizontalPathEdges(path As GridLinePath, width As Integer, height As Integer, fallbackY As Single)
        If path.Points.Count = 0 Then
            path.Points.Add(New PointF(0, Clamp(fallbackY, 0, height - 1)))
            path.Points.Add(New PointF(width - 1, Clamp(fallbackY, 0, height - 1)))
            Return
        End If

        path.Points = path.Points.OrderBy(Function(point) point.X).ToList()
        Dim first = path.Points(0)
        Dim last = path.Points(path.Points.Count - 1)
        If first.X > 0 Then path.Points.Insert(0, New PointF(0, first.Y))
        If last.X < width - 1 Then path.Points.Add(New PointF(width - 1, last.Y))
    End Sub

    Private Shared Sub EnsureVerticalPathEdges(path As GridLinePath, width As Integer, height As Integer, fallbackX As Single)
        If path.Points.Count = 0 Then
            path.Points.Add(New PointF(Clamp(fallbackX, 0, width - 1), 0))
            path.Points.Add(New PointF(Clamp(fallbackX, 0, width - 1), height - 1))
            Return
        End If

        path.Points = path.Points.OrderBy(Function(point) point.Y).ToList()
        Dim first = path.Points(0)
        Dim last = path.Points(path.Points.Count - 1)
        If first.Y > 0 Then path.Points.Insert(0, New PointF(first.X, 0))
        If last.Y < height - 1 Then path.Points.Add(New PointF(last.X, height - 1))
    End Sub

    Private Shared Function SmoothPath(path As GridLinePath, isHorizontal As Boolean) As GridLinePath
        If path.Points.Count < 5 Then Return path

        Dim source = If(isHorizontal,
                        path.Points.OrderBy(Function(point) point.X).ToList(),
                        path.Points.OrderBy(Function(point) point.Y).ToList())
        Dim result As New GridLinePath()
        result.Points.Add(source(0))

        For index = 1 To source.Count - 2
            Dim previous = source(index - 1)
            Dim current = source(index)
            Dim following = source(index + 1)
            If isHorizontal Then
                result.Points.Add(New PointF(current.X, (previous.Y + current.Y * 2.0F + following.Y) / 4.0F))
            Else
                result.Points.Add(New PointF((previous.X + current.X * 2.0F + following.X) / 4.0F, current.Y))
            End If
        Next

        result.Points.Add(source(source.Count - 1))
        Return result
    End Function

    Private Shared Function AveragePathY(path As GridLinePath) As Single
        If path.Points.Count = 0 Then Return 0
        Return CSng(path.Points.Average(Function(point) point.Y))
    End Function

    Private Shared Function AveragePathX(path As GridLinePath) As Single
        If path.Points.Count = 0 Then Return 0
        Return CSng(path.Points.Average(Function(point) point.X))
    End Function

    Private Shared Function MergePaths(first As GridLinePath, second As GridLinePath) As GridLinePath
        Dim result As New GridLinePath()
        Dim count = Math.Min(first.Points.Count, second.Points.Count)
        If count = 0 Then Return If(first.Points.Count > 0, first, second)

        For index = 0 To count - 1
            result.Points.Add(New PointF(
                (first.Points(index).X + second.Points(index).X) / 2.0F,
                (first.Points(index).Y + second.Points(index).Y) / 2.0F))
        Next

        Return result
    End Function

    Private Shared Function FitHorizontalSegments(gridMask As Boolean(), width As Integer, height As Integer, centers As IReadOnlyList(Of Single)) As List(Of GridLineSegment)
        Dim result As New List(Of GridLineSegment)
        Dim searchRadius = Math.Max(5, height \ 70)
        Dim sampleStep = Math.Max(2, width \ 450)
        Dim minimumPoints = Math.Max(16, (width \ sampleStep) \ 8)

        For Each center In centers
            Dim points As New List(Of PointF)
            Dim centerY = CInt(Math.Round(center))
            For x As Integer = 0 To width - 1 Step sampleStep
                Dim bestY = -1
                Dim bestScore = 0
                Dim top = Math.Max(0, centerY - searchRadius)
                Dim bottom = Math.Min(height - 1, centerY + searchRadius)
                For y As Integer = top To bottom
                    Dim score = CountHorizontalSupport(gridMask, width, height, x, y)
                    If score > bestScore Then
                        bestScore = score
                        bestY = y
                    End If
                Next
                If bestY >= 0 AndAlso bestScore >= 3 Then points.Add(New PointF(x, bestY))
            Next
            result.Add(FitYFromX(points, width, height, center, minimumPoints))
        Next

        Return result
    End Function

    Private Shared Function RemoveDuplicateHorizontalSegments(segments As IReadOnlyList(Of GridLineSegment)) As List(Of GridLineSegment)
        Dim result As New List(Of GridLineSegment)
        If segments Is Nothing OrElse segments.Count = 0 Then Return result

        Dim sorted = segments.OrderBy(Function(segment) AverageY(segment)).ToList()
        If sorted.Count <= 2 Then Return sorted

        Dim gaps As New List(Of Single)
        For index = 1 To sorted.Count - 1
            Dim gap = AverageY(sorted(index)) - AverageY(sorted(index - 1))
            If gap > 0.1F Then gaps.Add(gap)
        Next
        If gaps.Count = 0 Then Return sorted

        gaps.Sort()
        Dim medianGap = gaps(gaps.Count \ 2)
        Dim duplicateDistance = Math.Max(6.0F, medianGap * 0.45F)

        For Each segment In sorted
            If result.Count = 0 Then
                result.Add(segment)
                Continue For
            End If

            Dim previous = result(result.Count - 1)
            If AverageY(segment) - AverageY(previous) <= duplicateDistance Then
                result(result.Count - 1) = MergeHorizontalSegments(previous, segment)
            Else
                result.Add(segment)
            End If
        Next

        Return result
    End Function

    Private Shared Function MergeHorizontalSegments(first As GridLineSegment, second As GridLineSegment) As GridLineSegment
        Return New GridLineSegment With {
            .X1 = (first.X1 + second.X1) / 2.0F,
            .Y1 = (first.Y1 + second.Y1) / 2.0F,
            .X2 = (first.X2 + second.X2) / 2.0F,
            .Y2 = (first.Y2 + second.Y2) / 2.0F
        }
    End Function

    Private Shared Function AverageY(segment As GridLineSegment) As Single
        Return (segment.Y1 + segment.Y2) / 2.0F
    End Function

    Private Shared Function FitVerticalSegments(gridMask As Boolean(), width As Integer, height As Integer, centers As IReadOnlyList(Of Single)) As List(Of GridLineSegment)
        Dim result As New List(Of GridLineSegment)
        Dim searchRadius = Math.Max(5, width \ 120)
        Dim sampleStep = Math.Max(2, height \ 350)
        Dim minimumPoints = Math.Max(14, (height \ sampleStep) \ 8)

        For Each center In centers
            Dim points As New List(Of PointF)
            Dim centerX = CInt(Math.Round(center))
            For y As Integer = 0 To height - 1 Step sampleStep
                Dim bestX = -1
                Dim bestScore = 0
                Dim left = Math.Max(0, centerX - searchRadius)
                Dim right = Math.Min(width - 1, centerX + searchRadius)
                For x As Integer = left To right
                    Dim score = CountVerticalSupport(gridMask, width, height, x, y)
                    If score > bestScore Then
                        bestScore = score
                        bestX = x
                    End If
                Next
                If bestX >= 0 AndAlso bestScore >= 3 Then points.Add(New PointF(bestX, y))
            Next
            result.Add(FitXFromY(points, width, height, center, minimumPoints))
        Next

        Return result
    End Function

    Private Shared Function FitYFromX(points As IReadOnlyList(Of PointF), width As Integer, height As Integer, fallbackY As Single, minimumPoints As Integer) As GridLineSegment
        If points.Count < minimumPoints Then
            Return New GridLineSegment With {.X1 = 0, .Y1 = Clamp(fallbackY, 0, height - 1), .X2 = width - 1, .Y2 = Clamp(fallbackY, 0, height - 1)}
        End If

        Dim sumX As Double = 0
        Dim sumY As Double = 0
        Dim sumXX As Double = 0
        Dim sumXY As Double = 0
        For Each point In points
            sumX += point.X
            sumY += point.Y
            sumXX += point.X * point.X
            sumXY += point.X * point.Y
        Next

        Dim count = CDbl(points.Count)
        Dim denominator = count * sumXX - sumX * sumX
        If Math.Abs(denominator) < 0.000001 Then
            Return New GridLineSegment With {.X1 = 0, .Y1 = Clamp(fallbackY, 0, height - 1), .X2 = width - 1, .Y2 = Clamp(fallbackY, 0, height - 1)}
        End If

        Dim slope = (count * sumXY - sumX * sumY) / denominator
        If Math.Abs(slope) > 0.2 Then
            Return New GridLineSegment With {.X1 = 0, .Y1 = Clamp(fallbackY, 0, height - 1), .X2 = width - 1, .Y2 = Clamp(fallbackY, 0, height - 1)}
        End If

        Dim intercept = (sumY - slope * sumX) / count
        Return New GridLineSegment With {
            .X1 = 0,
            .Y1 = Clamp(CSng(intercept), 0, height - 1),
            .X2 = width - 1,
            .Y2 = Clamp(CSng(slope * (width - 1) + intercept), 0, height - 1)
        }
    End Function

    Private Shared Function FitXFromY(points As IReadOnlyList(Of PointF), width As Integer, height As Integer, fallbackX As Single, minimumPoints As Integer) As GridLineSegment
        If points.Count < minimumPoints Then
            Return New GridLineSegment With {.X1 = Clamp(fallbackX, 0, width - 1), .Y1 = 0, .X2 = Clamp(fallbackX, 0, width - 1), .Y2 = height - 1}
        End If

        Dim sumX As Double = 0
        Dim sumY As Double = 0
        Dim sumYY As Double = 0
        Dim sumXY As Double = 0
        For Each point In points
            sumX += point.X
            sumY += point.Y
            sumYY += point.Y * point.Y
            sumXY += point.X * point.Y
        Next

        Dim count = CDbl(points.Count)
        Dim denominator = count * sumYY - sumY * sumY
        If Math.Abs(denominator) < 0.000001 Then
            Return New GridLineSegment With {.X1 = Clamp(fallbackX, 0, width - 1), .Y1 = 0, .X2 = Clamp(fallbackX, 0, width - 1), .Y2 = height - 1}
        End If

        Dim slope = (count * sumXY - sumX * sumY) / denominator
        If Math.Abs(slope) > 0.2 Then
            Return New GridLineSegment With {.X1 = Clamp(fallbackX, 0, width - 1), .Y1 = 0, .X2 = Clamp(fallbackX, 0, width - 1), .Y2 = height - 1}
        End If

        Dim intercept = (sumX - slope * sumY) / count
        Return New GridLineSegment With {
            .X1 = Clamp(CSng(intercept), 0, width - 1),
            .Y1 = 0,
            .X2 = Clamp(CSng(slope * (height - 1) + intercept), 0, width - 1),
            .Y2 = height - 1
        }
    End Function

    Private Shared Function CountHorizontalSupport(gridMask As Boolean(), width As Integer, height As Integer, x As Integer, y As Integer) As Integer
        Dim count = 0
        For dy As Integer = -1 To 1
            For dx As Integer = -3 To 3
                If IsMaskSet(gridMask, width, height, x + dx, y + dy) Then count += 1
            Next
        Next
        Return count
    End Function

    Private Shared Function CountVerticalSupport(gridMask As Boolean(), width As Integer, height As Integer, x As Integer, y As Integer) As Integer
        Dim count = 0
        For dy As Integer = -3 To 3
            For dx As Integer = -1 To 1
                If IsMaskSet(gridMask, width, height, x + dx, y + dy) Then count += 1
            Next
        Next
        Return count
    End Function

    Private Shared Function IsMaskSet(gridMask As Boolean(), width As Integer, height As Integer, x As Integer, y As Integer) As Boolean
        If x < 0 OrElse x >= width OrElse y < 0 OrElse y >= height Then Return False
        Return gridMask(y * width + x)
    End Function

    Private Shared Function Clamp(value As Single, minimum As Integer, maximum As Integer) As Single
        If value < minimum Then Return minimum
        If value > maximum Then Return maximum
        Return value
    End Function
End Class
