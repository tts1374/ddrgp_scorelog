-- Shared-master update supplies the reviewed song category, independently of GP display version.
ALTER TABLE songs ADD COLUMN flare_category TEXT
    CHECK (flare_category IN ('CLASSIC', 'WHITE', 'GOLD'));

-- Keep existing AC-folder classification until the next shared-master import.
UPDATE songs SET flare_category = CASE
    WHEN version IN ('DDR 1st','DDR 2ndMIX','DDR 3rdMIX','DDR 4thMIX','DDR 5thMIX',
        'DDRMAX','DDRMAX2','DDR EXTREME','DDR SuperNOVA','DDR SuperNOVA 2',
        'DDR X','DDR X2','DDR X3 VS 2ndMIX') THEN 'CLASSIC'
    WHEN version IN ('DanceDanceRevolution (2013)','DanceDanceRevolution (2014)',
        'DanceDanceRevolution A') THEN 'WHITE'
    WHEN version IN ('DanceDanceRevolution A20','DanceDanceRevolution A20 PLUS',
        'DanceDanceRevolution A20 PL US','DanceDanceRevolution A3',
        'DanceDanceRevolution WORLD') THEN 'GOLD'
    ELSE NULL
END;
