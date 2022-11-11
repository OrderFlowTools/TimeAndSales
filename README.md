# TimeAndSales SuperDOM Column
Time and Sales Column for the Ninjtrader v8 SuperDOM
 
>Giving credit where credit is due. This custom time and sales column 
for the SuperDOM is built on the original code published by NinjaTrader_ZacharyG 
on the Ninjatrader Ecosystem.

  
**This version has been modified to include:**
- Regular and Filtered T&S panels
- Option to turn off Timestamp
- Display full or shortened price (ex. 89.40 instead of 4289.40)
- Detect potential ICE executions (that exceed bid/ask sizes)
- Trade aggregation
   - Accumulating trades till the price or trade type (at bid/at ask/above etc) changes.

This version also implements limiting T&S data held in memory during the session, to prevent memory problems.
 
Enjoy!

## Screenshots
### Default configuration: Price, Size, [Block, Ice], Timestamp and a Filtered trades subpanel.
![Full Config](https://github.com/OrderFlowTools/TimeAndSales/blob/main/screenshots/time_sales_config_1.PNG)

### Without Timestamps
![No Timestamps](https://github.com/OrderFlowTools/TimeAndSales/blob/main/screenshots/time_sales_config_2.PNG)

### Shortened Price, Without Timestamps
![Shortened Price, No Timestamps](https://github.com/OrderFlowTools/TimeAndSales/blob/main/screenshots/time_sales_config_3.PNG)
