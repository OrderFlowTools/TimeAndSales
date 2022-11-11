#region Using declarations
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.SuperDom;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using Matrix = System.Windows.Media.Matrix;
using Point = System.Windows.Point;

#endregion

/**
 * Giving credit where credit is due. This custom time and sales column 
 * for the SuperDOM is built on the original code published by NinjaTrader_ZacharyG here:
 * https://ninjatraderecosystem.com/user-app-share-download/mini-time-sales-superdom-column/
 * 
 * This version has been modified to include :
 *     - An additional filtered trades panel (configurable length)
 *     - Option to turn off Timestamp
 *     - Display full or shortened price (ex. 89.40 instead of 4289.40)
 *     - Detect potential ICE executions (above bid/ask size)
 *     - Trade aggregation
 *        - This is based on accumulating trades till the price 
 *          or trade type (at bid/at ask/above etc) changes.
 *     - Fixed size List to prevent memory issues
 * 
 * Enjoy!
 * Gem Immanuel (gemify@gmail.com)
 */
namespace NinjaTrader.NinjaScript.SuperDomColumns
{
    /// <summary>
    /// Class that represents a Time & Sales entry.
    /// </summary>
    class TSEntry
    {
        public double Price { get; set; }
        public double Size { get; set; }
        public DateTime Time { get; set; }
        public TSEntryType EntryType { get; set; }
        public bool Ice { get; set; }
    }

    /// <summary>
    /// Enumeration of the various T&S entry types.
    /// </summary>
    public enum TSEntryType
    {
        AT_BID,
        AT_ASK,
        ABOVE_ASK,
        BELOW_BID,
        BETWEEN_BID_ASK,
        UNKNOWN
    }

    [Gui.CategoryOrder("Display", 1)]
    [Gui.CategoryOrder("Parameters", 2)]
    [Gui.CategoryOrder("Colors", 3)]
    public class TimeAndSalesColumn : SuperDomColumn
    {
        private Typeface typeface;
        private CultureInfo culture;
        private List<TSEntry> tsEntries;
        private List<TSEntry> tsFilteredEntries;
        private readonly double startY = 2;

        private TSEntryType lastTradeType = TSEntryType.UNKNOWN;
        private double lastTradePrice = 0;
        private long consoliatedSize = 0;

        private Pen gridPen;
        private double halfPenWidth;
        private double panelHeight;
        private Brush FilteredHeaderBrush;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Time and Sales SuperDOM column";
                Name = "Time & Sales";
                DefaultWidth = 200;
                tsEntries = new List<TSEntry>();
                tsFilteredEntries = new List<TSEntry>();
                BackColor = Application.Current.TryFindResource("brushPriceColumnBackground") as SolidColorBrush;
                FilteredHeaderBrush = Brushes.Maroon;
                AtBidColor = Brushes.Firebrick;
                AtAskColor = Brushes.RoyalBlue;
                AboveAskColor = Brushes.DodgerBlue;
                BelowBidColor = Brushes.Red;
                BetweenColor = Brushes.DimGray;
                BlockSize = 80;
                TradeFilterSize = 20;

                AggregateTrades = false;
                DisplayTime = true;
                DisplayFullPrice = true;
                DisplayFilteredPanel = true;

                // Filtered panel takes up 40% of SuperDom columns
                FilteredPanelSizePerc = 40;

                culture = Core.Globals.GeneralOptions.CurrentCulture;

            }
            else if (State == State.Configure)
            {
                typeface = new Typeface(SuperDom.Font.Family, SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal, SuperDom.Font.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            }
        }


        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last) return;

            TSEntryType currentTradeType = GetMatchType(e);
            double currentPrice = e.Price;

            // If aggregating volume
            if (AggregateTrades)
            {
                // If trade type or price has changed, aggregation is ended
                if (currentTradeType != lastTradeType || currentPrice != lastTradePrice)
                {
                    // Add the time & sales entry
                    if (lastTradeType != TSEntryType.UNKNOWN)
                    {
                        bool isIce = IsIce(e, currentTradeType);

                        TSEntry entry = new TSEntry { Price = lastTradePrice, Size = consoliatedSize, Time = e.Time, EntryType = lastTradeType, Ice = isIce };
                        AddTSEntry(tsEntries, entry);
                        // Add filtered entry if needed
                        if (DisplayFilteredPanel && entry.Size > TradeFilterSize)
                        {
                            AddTSEntry(tsFilteredEntries, entry);
                        }
                    }

                    // Reset consolidatedSize, current price and type
                    consoliatedSize = e.Volume;
                    lastTradePrice = currentPrice;
                    lastTradeType = currentTradeType;
                }
                else
                {
                    // Aggregate volume
                    consoliatedSize += e.Volume;
                }
            }
            else
            {
                bool isIce = IsIce(e, currentTradeType);

                // Add the time & sales entry
                TSEntry entry = new TSEntry { Price = e.Price, Size = e.Volume, Time = e.Time, EntryType = currentTradeType, Ice = isIce };
                AddTSEntry(tsEntries, entry);
                // Add filtered entry if needed
                if (DisplayFilteredPanel && entry.Size > TradeFilterSize)
                {
                    AddTSEntry(tsFilteredEntries, entry);
                }
            }
        }

        private bool IsIce(MarketDataEventArgs e, TSEntryType currentTradeType)
        {
            bool isIce = false;
            if (!AggregateTrades)
            {
                switch (currentTradeType)
                {
                    case TSEntryType.AT_BID:
                        isIce = SuperDom.MarketDepth.Bids.Count > 0 ? e.Volume > SuperDom.MarketDepth.Bids[0].Volume : false;
                        break;
                    case TSEntryType.AT_ASK:
                        isIce = SuperDom.MarketDepth.Asks.Count > 0 ? e.Volume > SuperDom.MarketDepth.Asks[0].Volume : false;
                        break;
                }
            }

            return isIce;
        }

        private void AddTSEntry<T>(List<T> entries, T entry)
        {
            entries.Add(entry);
            // Clear out entries from data list if they exceed the number of SuperDOM rows
            // This will prevent the list from growing indefinitely.
            // Entries will AT MOST be equal to the number of SuperDOM columns.
            if (entries.Count > SuperDom.Rows.Count)
            {
                entries.RemoveRange(0, entries.Count - SuperDom.Rows.Count);
            }
        }

        private TSEntryType GetMatchType(MarketDataEventArgs e)
        {
            TSEntryType currentTradeType = TSEntryType.UNKNOWN;

            if (e.Price == e.Ask)
            {
                currentTradeType = TSEntryType.AT_ASK;
            }
            else if (e.Price == e.Bid)
            {
                currentTradeType = TSEntryType.AT_BID;
            }
            else if (e.Price > e.Ask)
            {
                currentTradeType = TSEntryType.ABOVE_ASK;
            }
            else if (e.Price < e.Bid)
            {
                currentTradeType = TSEntryType.BELOW_BID;
            }
            else if (e.Price > e.Bid && e.Price < e.Ask)
            {
                currentTradeType = TSEntryType.BETWEEN_BID_ASK;
            }

            return currentTradeType;
        }

        protected override void OnRender(DrawingContext dc, double renderWidth)
        {
            double dpiFactor = 1;
            if (gridPen == null)
            {
                if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
                {
                    Matrix m = PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
                    dpiFactor = 1 / m.M11;
                    gridPen = new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush, 1 * dpiFactor);
                    halfPenWidth = gridPen.Thickness * 0.5;
                }
            }

            double verticalOffset = -gridPen.Thickness;

            // Fill full panel with background color
            DrawFullPanel(dc, renderWidth, verticalOffset);

            int unfilteredEndRow = SuperDom.Rows.Count;
            double subPanelY = 0;

            if (DisplayFilteredPanel) {

                // Calculate Filtered SubPanel starting row / unfiltered end row
                unfilteredEndRow = (int)((double)SuperDom.Rows.Count * (1.0-(FilteredPanelSizePerc/100.0)));
                
                // Adjust for header row
                unfilteredEndRow = unfilteredEndRow > 1 ? unfilteredEndRow-- : unfilteredEndRow;
                
                // Calculate Filtered SubPanel header row's starting Y coordinate
                int subPanelHeaderPoint = (int)SuperDom.ActualRowHeight * unfilteredEndRow;

                // Calculate y coordinate of where the subpanel data starts
                subPanelY = subPanelHeaderPoint + SuperDom.ActualRowHeight + gridPen.Thickness;

                // Display filtered panel header
                Rect subPanelHeaderRect = new Rect(-halfPenWidth, subPanelHeaderPoint, renderWidth - halfPenWidth, SuperDom.ActualRowHeight);
                dc.DrawRectangle(FilteredHeaderBrush, null, subPanelHeaderRect);

                FormattedText subPanelHeader = new FormattedText("FILTERED (" + TradeFilterSize + ")", Core.Globals.GeneralOptions.CurrentCulture, FlowDirection.LeftToRight, typeface, SuperDom.Font.Size + 1, Brushes.Silver, dpiFactor) { MaxLineCount = 1, MaxTextWidth = (renderWidth < 11 ? 1 : renderWidth - 10), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
                dc.DrawText(subPanelHeader, new Point(4, subPanelHeaderPoint + dpiFactor * 4));
            }

            // Display T&S Entries 
            DisplayTSEntries(dc, renderWidth, dpiFactor, startY, tsEntries, unfilteredEndRow);

            // Display Filtered T&S Entries
            if (DisplayFilteredPanel)
            {
                DisplayTSEntries(dc, renderWidth, dpiFactor, subPanelY, tsFilteredEntries, SuperDom.Rows.Count - unfilteredEndRow - 1);
            }

        }

        private void DrawFullPanel(DrawingContext dc, double renderWidth, double verticalOffset)
        {
            panelHeight = SuperDom.ActualRowHeight * SuperDom.Rows.Count;
            Rect rect = new Rect(-halfPenWidth, verticalOffset, renderWidth - halfPenWidth, panelHeight);
            dc.DrawRectangle(BackColor, gridPen, rect);
        }

        private void DisplayTSEntries(DrawingContext dc, double renderWidth, double dpiFactor, double rowY, List<TSEntry> data, int lastRow)
        {
            int row = 0;

            for (int i = data.Count - 1; i > 0; i--)
            {
                TSEntry tsEntry = data[i];

                String entryText = CreateEntryText(tsEntry);

                // Format the text and draw it
                double x = 10;
                FormattedText ftext = FormatText(entryText, tsEntry.EntryType, TextAlignment.Left, renderWidth, dpiFactor);

                // Draw unfiltered entry
                if (row < lastRow)
                {
                    dc.DrawText(ftext, new Point(x, rowY));

                    rowY += SuperDom.ActualRowHeight;
                    row++;
                }
            }
        }

        private string CreateEntryText(TSEntry tsEntry)
        {
            StringBuilder sb = new StringBuilder();
            String price = tsEntry.Price.ToString("#.00");
            if (!DisplayFullPrice)
            {   
                // Shorten the price, by extracting the first two digits
                Match match = Regex.Match(price, @"(\d{0,2}\.\d+)$");
                if (match.Success)
                {
                    price = match.Groups[1].Value;
                }
            }
            sb.Append(price);
            sb.Append("\t");
            sb.Append(String.Format("{0}\t{1}\t{2}", tsEntry.Size.ToString("N0").PadLeft(10), tsEntry.Size > BlockSize ? "BLK" : " ", tsEntry.Ice ? "ICE" : " "));
            if (DisplayTime)
            {
                sb.Append("\t");
                sb.Append(tsEntry.Time.ToLongTimeString());
            }

            String entryText = sb.ToString();
            return entryText;
        }

        private FormattedText FormatText(String text, TSEntryType entryType, TextAlignment textAlignment, double renderWidth, double dpiFactor)
        {
            FormattedText ft = null;
            if (renderWidth - 6 > 0)
            {
                Brush textColor = Brushes.Transparent;
                switch (entryType)
                {
                    case TSEntryType.AT_BID: textColor = AtBidColor; break;
                    case TSEntryType.AT_ASK: textColor = AtAskColor; break;
                    case TSEntryType.ABOVE_ASK: textColor = AboveAskColor; break;
                    case TSEntryType.BELOW_BID: textColor = BelowBidColor; break;
                    case TSEntryType.BETWEEN_BID_ASK: textColor = BetweenColor; break;
                }
                ft = new FormattedText(text.ToString(culture), culture, FlowDirection.LeftToRight, typeface, SuperDom.Font.Size + 1, textColor, dpiFactor) { MaxLineCount = 1, MaxTextWidth = (renderWidth < 11 ? 1 : renderWidth - 10), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = textAlignment };
            }
            return ft;
        }

        #region Properties
        [XmlIgnore]
        [Display(Name = "Background", Order = 1, GroupName = "Colors")]
        public Brush BackColor
        { get; set; }

        [Browsable(false)]
        public string BackColorSerializable
        {
            get { return Serialize.BrushToString(BackColor); }
            set { BackColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "At Ask", Order = 2, GroupName = "Colors")]
        public Brush AtAskColor
        { get; set; }

        [Browsable(false)]
        public string AtAskColorSerializable
        {
            get { return Serialize.BrushToString(AtAskColor); }
            set { AtAskColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "At Bid", Order = 3, GroupName = "Colors")]
        public Brush AtBidColor
        { get; set; }

        [Browsable(false)]
        public string AtBidColorSerializable
        {
            get { return Serialize.BrushToString(AtBidColor); }
            set { AtBidColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Above Ask", Order = 4, GroupName = "Colors")]
        public Brush AboveAskColor
        { get; set; }

        [Browsable(false)]
        public string AboveAskColorSerializable
        {
            get { return Serialize.BrushToString(AboveAskColor); }
            set { AboveAskColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Below Bid", Order = 5, GroupName = "Colors")]
        public Brush BelowBidColor
        { get; set; }

        [Browsable(false)]
        public string BelowBidColorSerializable
        {
            get { return Serialize.BrushToString(BelowBidColor); }
            set { BelowBidColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Between Ask and Bid", Order = 6, GroupName = "Colors")]
        public Brush BetweenColor
        { get; set; }

        [Browsable(false)]
        public string BetweenColorSerializable
        {
            get { return Serialize.BrushToString(BetweenColor); }
            set { BetweenColor = Serialize.StringToBrush(value); }
        }


        [Display(Name = "Aggregate Trades", Order = 1, GroupName = "Parameters")]
        public bool AggregateTrades
        { get; set; }

        [Display(Name = "Trade Filter Size", Order = 2, GroupName = "Parameters")]
        public int TradeFilterSize
        { get; set; }

        [Display(Name = "Block Trade Size", Order = 3, GroupName = "Parameters")]
        public int BlockSize
        { get; set; }


        [Display(Name = "Display Timestamp", Order = 1, GroupName = "Display")]
        public bool DisplayTime
        { get; set; }

        [Display(Name = "Display Full Price", Order = 2, GroupName = "Display")]
        public bool DisplayFullPrice
        { get; set; }

        [Display(Name = "Display Filtered SubPanel", Order = 3, GroupName = "Display")]
        public bool DisplayFilteredPanel
        { get; set; }

        [Range(20.0, 80.0)]
        [Display(Name = "Filtered Panel Size (% of SuperDOM rows)", Description = "Min: 20%, Max: 80%. Size of filtered panel as % of the SuperDom Rows.", Order = 4, GroupName = "Display")]
        public double FilteredPanelSizePerc
        { get; set; }

        #endregion
    }
}
