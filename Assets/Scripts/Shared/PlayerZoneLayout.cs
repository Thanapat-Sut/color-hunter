using UnityEngine;

// Pure geometry helpers for the multi-player zone-division layouts (Pilot 2026-08-26).
// Extracted so QuizZone, LuckCastle, Rhythm Stomp, Snake Wars can share one implementation
// of tile → (player, answer) resolution. No Unity object refs — takes cols/rows/playerCount
// as arguments and returns indexes.
//
// Layout summary (cols=10, rows=18 typical):
//   1 player: whole field, quadrants 0..3 = answers A..D.
//   2 players: horizontal split with CenterlineRows dead strip between halves; each half is
//     a 2x2 button grid (TL=A, TR=B, BL=C, BR=D). Both halves share the same orientation.
//   3-4 players: cross-shaped dead strip (CenterlineRows horizontal + CenterlineCols vertical)
//     splits field into 4 equal quadrants. Each quadrant is a 2x2 button grid. Player 0=TL,
//     1=TR, 2=BL, 3=BR. In 3-player mode the BR quadrant is a dead zone.
public static class PlayerZoneLayout
{
    public const int CenterlineRows = 2;
    public const int CenterlineCols = 2;

    // A single middle row on odd-height floors preserves equal eight-row halves
    // at both 10x17 and 10x18. Single-player only needs a divider for odd heights.
    static int CenterRows(int rows, int playerCount)
    {
        return playerCount <= 1 ? rows % 2 : (rows % 2 == 0 ? CenterlineRows : 1);
    }

    public static bool IsCenterlineTile(int visualCol, int visualRow, int cols, int rows, int playerCount)
    {
        int centerRows = CenterRows(rows, playerCount);
        if (playerCount <= 1)
            return centerRows > 0 && visualRow == rows / 2;

        if (playerCount == 2)
        {
            int halfHeight = (rows - centerRows) / 2;
            if (halfHeight <= 0) return false;
            return visualRow >= halfHeight && visualRow < halfHeight + centerRows;
        }

        if (playerCount == 3 || playerCount == 4)
        {
            int halfHeight = (rows - centerRows) / 2;
            int halfWidth = (cols - CenterlineCols) / 2;
            if (halfHeight <= 0 || halfWidth <= 0) return false;
            bool inHorizontal = visualRow >= halfHeight && visualRow < halfHeight + centerRows;
            bool inVertical = visualCol >= halfWidth && visualCol < halfWidth + CenterlineCols;
            return inHorizontal || inVertical;
        }

        return false;
    }

    public static int GetPlayerForTile(int visualCol, int visualRow, int cols, int rows, int playerCount)
    {
        if (IsCenterlineTile(visualCol, visualRow, cols, rows, playerCount)) return -1;
        if (playerCount <= 1) return 0;
        int centerRows = CenterRows(rows, playerCount);

        if (playerCount == 2)
        {
            int halfHeight = (rows - centerRows) / 2;
            if (halfHeight <= 0) return -1;
            int centerlineEnd = halfHeight + centerRows;
            if (visualRow >= halfHeight && visualRow < centerlineEnd) return -1;
            return visualRow < halfHeight ? 0 : 1;
        }

        int halfHeightQ = (rows - centerRows) / 2;
        int halfWidth = (cols - CenterlineCols) / 2;
        if (halfHeightQ <= 0 || halfWidth <= 0) return -1;
        if (visualRow >= halfHeightQ && visualRow < halfHeightQ + centerRows) return -1;
        if (visualCol >= halfWidth && visualCol < halfWidth + CenterlineCols) return -1;

        bool topHalfQ = visualRow < halfHeightQ;
        bool leftHalfQ = visualCol < halfWidth;
        int quad = topHalfQ ? (leftHalfQ ? 0 : 1) : (leftHalfQ ? 2 : 3);
        if (playerCount == 3 && quad == 3) return -1;
        return quad;
    }

    // Each player's whole area (answer buttons + dead tiles), centerline excluded.
    public static bool TryGetPlayerArea(int player, int cols, int rows, int playerCount, out RectInt area)
    {
        area = default;
        if (player < 0) return false;
        if (playerCount <= 1)
        {
            if (player != 0) return false;
            area = new RectInt(0, 0, cols, rows);
            return true;
        }
        int centerRows = CenterRows(rows, playerCount);
        int halfHeight = (rows - centerRows) / 2;
        if (halfHeight <= 0) return false;
        int bottomY = halfHeight + centerRows;
        if (playerCount == 2)
        {
            if (player > 1) return false;
            area = new RectInt(0, player == 0 ? 0 : bottomY, cols, halfHeight);
            return true;
        }
        if (playerCount == 3 || playerCount == 4)
        {
            if (player >= playerCount) return false;
            int halfWidth = (cols - CenterlineCols) / 2;
            if (halfWidth <= 0) return false;
            bool left = player == 0 || player == 2;
            bool top = player < 2;
            area = new RectInt(left ? 0 : halfWidth + CenterlineCols, top ? 0 : bottomY, halfWidth, halfHeight);
            return true;
        }
        return false;
    }

    // Standing spot (Quiz/Luck LQ-07, EX-01): 2 columns wide at the middle of the player's area,
    // 2 rows tall (3 on an odd-height area — the 1P field straddles its middle row). The answer
    // buttons sit around it, so every answer starts the same distance away.
    public static bool TryGetSpot(int player, int cols, int rows, int playerCount, out RectInt spot)
    {
        spot = default;
        if (!TryGetPlayerArea(player, cols, rows, playerCount, out var a) || a.width < 2 || a.height < 2) return false;
        int h = a.height % 2 == 0 ? 2 : 3;
        spot = new RectInt(a.x + a.width / 2 - 1, a.y + (a.height - h) / 2, 2, h);
        return true;
    }

    // Player whose standing spot covers this tile, -1 if none.
    public static int GetSpotPlayer(int visualCol, int visualRow, int cols, int rows, int playerCount)
    {
        int n = Mathf.Clamp(playerCount, 1, 4);
        for (int p = 0; p < n; p++)
            if (TryGetSpot(p, cols, rows, playerCount, out var s) && s.Contains(new Vector2Int(visualCol, visualRow)))
                return p;
        return -1;
    }

    public static int GetAnswerForTile(int visualCol, int visualRow, int cols, int rows,
                                       int playerCount, int zoneCount)
    {
        if (zoneCount <= 0) return -1;
        if (IsCenterlineTile(visualCol, visualRow, cols, rows, playerCount)) return -1;
        int centerRows = CenterRows(rows, playerCount);

        // 1-player: whole-field quadrant = answer.
        if (playerCount <= 1)
        {
            bool isLeft = visualCol < cols / 2;
            bool isTop = visualRow < rows / 2;
            int quad = isTop ? (isLeft ? 0 : 1) : (isLeft ? 2 : 3);
            return (quad < zoneCount) ? quad : -1;
        }

        // 2-player horizontal split: each half is a 2x2 button grid. Same orientation on both
        // halves (mirror removed per Pilot 2026-08-26 revision): TL=A, TR=B, BL=C, BR=D.
        if (playerCount == 2)
        {
            int halfHeight = (rows - centerRows) / 2;
            if (halfHeight <= 0) return -1;
            int centerlineEnd = halfHeight + centerRows;
            if (visualRow >= halfHeight && visualRow < centerlineEnd) return -1;

            bool isTopHalf = visualRow < halfHeight;
            int rowInHalf = isTopHalf ? visualRow : (visualRow - centerlineEnd);
            int subRow = rowInHalf < halfHeight / 2 ? 0 : 1;
            int subCol = visualCol < cols / 2 ? 0 : 1;
            int ans = subRow * 2 + subCol;
            return (ans < zoneCount) ? ans : -1;
        }

        // 3-4 player: cross-shaped centerline splits field into 4 equal quadrants; each is 2x2.
        int halfHeight3 = (rows - centerRows) / 2;
        int halfWidth3 = (cols - CenterlineCols) / 2;
        if (halfHeight3 <= 0 || halfWidth3 <= 0) return -1;
        int centerlineRowEnd = halfHeight3 + centerRows;
        int centerlineColEnd = halfWidth3 + CenterlineCols;

        if (visualRow >= halfHeight3 && visualRow < centerlineRowEnd) return -1;
        if (visualCol >= halfWidth3 && visualCol < centerlineColEnd) return -1;

        bool leftHalf = visualCol < halfWidth3;
        bool topHalf = visualRow < halfHeight3;
        int quadPlayer = topHalf ? (leftHalf ? 0 : 1) : (leftHalf ? 2 : 3);
        if (playerCount == 3 && quadPlayer == 3) return -1;

        int colInZone = leftHalf ? visualCol : (visualCol - centerlineColEnd);
        int rowInZone = topHalf ? visualRow : (visualRow - centerlineRowEnd);
        int subCol2 = colInZone < halfWidth3 / 2 ? 0 : 1;
        int subRow2 = rowInZone < halfHeight3 / 2 ? 0 : 1;
        int ans2 = subRow2 * 2 + subCol2;
        return (ans2 < zoneCount) ? ans2 : -1;
    }
}
