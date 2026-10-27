CREATE INDEX IF NOT EXISTS IX_SalesInvoiceLines_Invoice ON SalesInvoiceLines(SalesInvoiceId, Id);
CREATE INDEX IF NOT EXISTS IX_SalesReturnLines_InvoiceLine ON SalesReturnLines(SalesInvoiceLineId);
