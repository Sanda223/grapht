CREATE TABLE dbo.Orders (
    Id INT PRIMARY KEY,
    Total DECIMAL(18, 2) NOT NULL
);
GO

CREATE PROCEDURE dbo.GetOrderTotals
AS
BEGIN
    SELECT Id, Total FROM dbo.Orders;
END
GO
