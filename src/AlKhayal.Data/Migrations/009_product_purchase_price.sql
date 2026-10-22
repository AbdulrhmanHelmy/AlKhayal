ALTER TABLE ProductDetails
ADD COLUMN PurchasePriceScaled INTEGER NULL
CHECK(PurchasePriceScaled IS NULL OR PurchasePriceScaled >= 0);
