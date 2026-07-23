SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- County Tax Collector reference lookup - same architecture as 053_circuit_clerks.sql /
-- 054_assessors.sql: a fixed, independent reference table with zero auth/identity dependency,
-- seeded with real data (source: portal.arkansas.gov county pages - no independent state-level
-- collector directory exists) so the office never has to hand-enter this per case. Not tied to
-- cases directly - cases just look a row up by their existing County column at read time, alongside
-- Circuit Clerk and Assessor, on the case workspace's combined "County Officials" panel. Idempotent
-- CREATE TABLE + IF NOT EXISTS seed, matching its siblings' pattern exactly. Unlike Circuit
-- Clerk/Assessor, [name] is nullable: Lafayette and Searcy counties have no collector name
-- published by the source (address/phone still known), so those two rows are seeded with a NULL
-- name and a note to verify with the county directly. Counties with multiple office locations (e.g.
-- Benton, Saline, Sebastian, Washington) are combined into one row with each office on its own line
-- in [address]. There is no live SQL Server sandbox available here to exercise this against a real
-- pilot instance - same limitation already noted for every other migration in this repo - so this
-- file has been reviewed for consistency with its siblings but not executed live.

IF OBJECT_ID(N'$(Schema).collectors','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[collectors]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_collectors] PRIMARY KEY,
        [county] nvarchar(100) NOT NULL CONSTRAINT [UQ_collectors_county] UNIQUE,
        [name] nvarchar(200) NULL,
        [address] nvarchar(1000) NULL,
        [phone] nvarchar(100) NULL,
        [notes] nvarchar(1000) NULL
    );
END;

IF NOT EXISTS(SELECT 1 FROM [$(Schema)].[collectors])
BEGIN
    INSERT INTO [$(Schema)].[collectors] ([county],[name],[address],[phone],[notes]) VALUES
        (N'Arkansas', N'Dean Mannis', N'101 Court Square, DeWitt, 72042' + NCHAR(10) + N'302 South College St, Stuttgart, 72160', N'870-659-2104', NULL),
        (N'Ashley', N'Lori Pennington', N'205 E Jefferson St, Hamburg, 71646', N'870-853-2050', NULL),
        (N'Baxter', N'Teresa Smith', N'8 East 7th Street, Mountain Home, 72653', N'870-425-8300', NULL),
        (N'Benton', N'Gloria Peterson', N'Bentonville: 2401 SW D. Street Ste 3, Bentonville, 72712' + NCHAR(10) + N'Gravette: 901 1st Ave SE (Hwy 59) Ste C, Gravette, 72736' + NCHAR(10) + N'Rogers: 2113 W Walnut St, Rogers, 72756' + NCHAR(10) + N'Siloam Springs: 707 Lincoln St, Siloam Springs, 72761', N'479-271-1040', NULL),
        (N'Boone', N'Amy Jenkins', N'220 N Arbor Drive, Harrison, 72601', N'870-741-6646', NULL),
        (N'Bradley', N'Wilmar Adair', N'101 East Cedar, Warren, 71671', N'870-226-3491', NULL),
        (N'Calhoun', N'Vernon Morris', N'109 South 2nd St., Hampton, 71744', N'870-798-2357', NULL),
        (N'Carroll', N'Kay Phillips-Brown', N'108 Spring St., Berryville, 72616', N'870-423-2867', N'No separate Eureka Springs collector office listed in the source.'),
        (N'Chicot', N'Gail Seamans', N'108 Main St., Lake Village, 71653', N'870-265-8030', NULL),
        (N'Clark', N'Jason C. Watson', N'401 Clay Street, Arkadelphia, 71923', N'870-246-2211', NULL),
        (N'Clay', N'Terry Miller', N'Clay County Courthouse, 2nd St., Piggott, 72454', N'870-598-2266', NULL),
        (N'Cleburne', N'Connie Caldwell', N'301 West Main Street, Heber Springs, 72543', N'501-362-8145', NULL),
        (N'Cleveland', N'Patti Wilson', N'20 Magnolia St., Rison, 71665', N'870-325-7254', NULL),
        (N'Columbia', N'Rachel Waller', N'P.O. Box 98, Magnolia, 71754', N'870-234-4171', NULL),
        (N'Conway', N'Norbert Gunderman, Jr.', N'117 South Moose Street, Morrilton, 72110', N'501-354-9600', NULL),
        (N'Craighead', N'Wes Eddington', N'511 Union Street, Suite 107, Jonesboro, 72401', N'870-933-4560', NULL),
        (N'Crawford', N'Kevin Pixley', N'300 Main St. #2, Van Buren, 72956', N'479-474-1111', NULL),
        (N'Crittenden', N'Ellen Foote', N'250 Pine, Ste. 2, Marion, 72364', N'870-739-3141', NULL),
        (N'Cross', N'Kristy Davis', N'705 E. Union Street, Wynne, 72396', N'870-238-5710', NULL),
        (N'Dallas', N'Brenda Williams-Black', N'206 West 3rd Street, Fordyce, 71742' + NCHAR(10) + N'P.O. Box 1024, Fordyce, 71742', N'870-352-5181', NULL),
        (N'Desha', N'Lisa Hutchison', N'604 President Street, Arkansas City, 71630', N'870-877-2525', NULL),
        (N'Drew', N'Tonya Loveless', N'210 S Main St, Monticello, 71655', N'870-460-6240', NULL),
        (N'Faulkner', N'Sherry Koonce', N'806 Faulkner Street, Conway, 72034', N'501-450-4921', NULL),
        (N'Franklin', N'Amy Harris', N'P.O. Box 1267, Ozark, 72949', N'479-667-4124', NULL),
        (N'Fulton', N'Michalle Watkins', N'P.O. Box 126, Salem, 72576', N'870-895-2457', NULL),
        (N'Garland', N'Rebecca Dodd-Talbert', N'200 Woodbine St #108, Hot Springs, 71901', N'501-622-3710', NULL),
        (N'Grant', N'Susan Whitehead', N'101 West Center, County Courthouse, Sheridan, 72150', N'870-942-4315', N'General county line: 870-942-2551'),
        (N'Greene', N'Cindy Tracer', N'320 West Court Street, Rm. 103, Paragould, 72450', N'870-239-6305', NULL),
        (N'Hempstead', N'James Singleton', N'400 South Washington, Hope, 71802' + NCHAR(10) + N'P.O. Box 549, Hope, 71802', N'870-777-4103', NULL),
        (N'Hot Spring', N'Valerie Hearn', N'210 Locust St., Malvern, 72104', N'501-332-5857', NULL),
        (N'Howard', N'Bryan McJunkins', N'P.O. Box 36, Nashville, 71852', N'870-845-7508', NULL),
        (N'Independence', N'Paul Albert', N'110 Broad Street, Batesville, 72501', N'870-793-8823', NULL),
        (N'Izard', N'Joshua Morehead', N'P.O. Box 490, Melbourne, 72556', N'870-368-7247', NULL),
        (N'Jackson', N'Kelly Walker', N'208 Main Street, Newport, 72112', N'870-523-7410', NULL),
        (N'Jefferson', N'Tony Washington', N'Jefferson County Courthouse, P.O. Drawer A, Pine Bluff, 71611', N'870-541-5313', NULL),
        (N'Johnson', N'Leta Willis', N'P.O. Box 344, Clarksville, 72830', N'479-754-3056', NULL),
        (N'Lafayette', NULL, N'6 Courthouse Square, Lewisville, 71845', N'870-921-4255', N'Name not published by the state source — verify with the county directly'),
        (N'Lawrence', N'Stephanie Harris', N'315 West Main, Walnut Ridge, 72476', N'870-886-1114', NULL),
        (N'Lee', N'Ocie Banks', N'15 E Chestnut St #1, Marianna, 72360', N'870-295-7752', NULL),
        (N'Lincoln', N'Melissa Bumpass', N'300 South Drew St. Room 102, Star City, 71667', N'870-628-5020', NULL),
        (N'Little River', N'Bobby Walraven', N'351 N 2nd Street, Suite 2, Ashdown, 71822', N'870-898-7216', NULL),
        (N'Logan', N'Brittany Porter', N'25 West Walnut St, Paris, 72855', N'479-963-2038', NULL),
        (N'Lonoke', N'Therese O''Donnell', N'208 N Center Street, Lonoke, 72086', N'501-676-6344', NULL),
        (N'Madison', N'DeAnna McElhaney', N'P.O. Box 1288, Huntsville, 72740', N'479-738-6673', NULL),
        (N'Marion', N'Cathy Brightwell', N'P.O. Box 590, Yellville, 72687' + NCHAR(10) + N'300 E. Old Main St., Yellville, 72687', N'870-449-6253', NULL),
        (N'Miller', N'Laura Bates', N'400 Laurel Street, Ste. 111, Texarkana, 71854', N'870-774-1001', NULL),
        (N'Mississippi', N'Susan McCormick', N'200 West Walnut St. Room 204, Blytheville, 72315', N'870-763-6841', NULL),
        (N'Monroe', N'Steve Mitchell', N'123 Madison, Clarendon, 72029', N'870-747-3722', NULL),
        (N'Montgomery', N'David E. White', N'105 U.S. 270, Mt. Ida, 71957', N'870-867-3271', NULL),
        (N'Nevada', N'Danny Martin', N'215 E. 2nd St. C Suite, Prescott, 71857', N'870-887-3511', NULL),
        (N'Newton', N'Nedra Daniels', N'100 E Court Street, Jasper, 72641', N'870-446-2378', NULL),
        (N'Ouachita', N'David Norwood', N'145 Jefferson St SW, Camden, 71701', N'870-837-2260', N'General county line: 870-837-2210'),
        (N'Perry', N'Scott Montgomery', N'310 West Main Street, Perryville, 72126', N'501-889-5285', NULL),
        (N'Phillips', N'Neal Byrd', N'620 Cherry Street, Suite 102, Helena, 72342', N'870-338-5580', N'General county line: 870-338-5500'),
        (N'Pike', N'Travis Hill', N'P.O. Box 217, Murfreesboro, 71958', N'870-285-3121', NULL),
        (N'Poinsett', N'Kevin Molder', N'401 Market St., Harrisburg, 72432', N'870-578-4415', N'General county line: 870-578-5333'),
        (N'Polk', N'Scott Sawyer', N'507 Church Avenue, Mena, 71953', N'479-394-8110', NULL),
        (N'Pope', N'Jennifer Haley', N'100 West Main, Russellville, 72801', N'479-968-7016', NULL),
        (N'Prairie', N'Rick Hickman', N'200 Courthouse Square Ste. 101, Des Arc, 72040', N'870-256-4137', NULL),
        (N'Pulaski', N'Debra Buckner', N'201 S. Broadway Suite 150, Little Rock, 72201', N'501-340-6040', NULL),
        (N'Randolph', N'Jennifer Zitzelberger', N'107 W Broadway St Ste H, Pocahontas, 72455', N'870-892-5491', NULL),
        (N'Saline', N'Holly Sanders', N'215 North Main, Suite 3, Benton, 72015' + NCHAR(10) + N'NW Third Street, Suite 101G, Bryant, 72202', N'501-303-5620', NULL),
        (N'Scott', N'Randy Shores', N'190 West 1st St., Box 14, Waldron, 72958', N'479-637-1017', NULL),
        (N'Searcy', NULL, N'P.O. Box 812, Marshall, 72650' + NCHAR(10) + N'106 W. Nome Street, Marshall, 72650', N'870-448-5050', N'Name not published by the state source — verify with the county directly'),
        (N'Sebastian', N'Lora Rice', N'Fort Smith: 35 South 6th, Room 112, Fort Smith, 72901' + NCHAR(10) + N'Greenwood: 301 E. Center St. Rm 112, Greenwood' + NCHAR(10) + N'Eastside: 6515 Phoenix Ave., Fort Smith, 72901', N'479-783-4163', NULL),
        (N'Sevier', N'Robert Gentry', N'115 N 3rd St., De Queen, 71832', N'870-642-2127', NULL),
        (N'Sharp', N'Michelle Daggett', N'P.O. Box 480, Ash Flat, 72513', N'870-994-7334', NULL),
        (N'St. Francis', N'Bobby May', N'P.O. Box 1817, Forrest City, 72336', N'870-261-1794', NULL),
        (N'Stone', N'Sue Younger', N'107 W. Main Street, Mountain View, 72560', N'870-269-2211', NULL),
        (N'Union', N'Karen Scott', N'101 North Washington Room 106, El Dorado, 71730', N'870-864-1930', NULL),
        (N'Van Buren', N'Laura Shannon', N'1414 Hwy. 65 South, Suite 118, Clinton, 72031' + NCHAR(10) + N'P.O. Box 359, Clinton, 72031', N'501-745-8550', NULL),
        (N'Washington', N'Angela Wood', N'280 N. College, Suite 202, Fayetteville, 72701' + NCHAR(10) + N'2250 West Sunset, Springdale, 72762' + NCHAR(10) + N'215 South Main, Lincoln, 72744', N'479-444-1526', NULL),
        (N'White', N'Beth Dorton', N'115 W Arch St, Searcy, 72143', N'501-279-6206', NULL),
        (N'Woodruff', N'Phil Reynolds', N'500 N 3rd St, Augusta, 72006', N'870-347-5152', NULL),
        (N'Yell', N'Bill Gilkey', N'Main Street, Danville, 72833', N'479-495-4868', N'General county line: 479-495-4850');
END;
