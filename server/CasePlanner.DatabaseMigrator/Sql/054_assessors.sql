SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- County Assessor reference lookup - same architecture as 053_circuit_clerks.sql: a fixed,
-- independent reference table with zero auth/identity dependency, seeded with real data (sources:
-- dfa.arkansas.gov's Assessment Coordination Division directory, primary; cross-checked against
-- portal.arkansas.gov county pages for additional office locations) so the office never has to
-- hand-enter this per case. Not tied to cases directly - cases just look a row up by their existing
-- County column at read time, alongside Circuit Clerk and Collector, on the case workspace's
-- combined "County Officials" panel. Idempotent CREATE TABLE + IF NOT EXISTS seed, matching
-- 053_circuit_clerks.sql's pattern exactly. Roughly 28 counties have a documented naming
-- discrepancy between dfa.arkansas.gov and portal.arkansas.gov (most likely turnover/elections one
-- source hasn't caught up on yet) - the DFA name is used as [name] per the office's instruction, and
-- the full caveat is preserved verbatim in [notes] rather than silently resolved one way, so staff
-- see it before relying on the name for a real notification. Counties with multiple office
-- locations (e.g. Benton, Sebastian) are combined into one row with each office on its own line in
-- [address], mirroring how circuit_clerks.address already handles Carroll County's two offices.
-- There is no live SQL Server sandbox available here to exercise this against a real pilot instance
-- - same limitation already noted for every other migration in this repo - so this file has been
-- reviewed for consistency with its siblings but not executed live.

IF OBJECT_ID(N'$(Schema).assessors','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[assessors]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_assessors] PRIMARY KEY,
        [county] nvarchar(100) NOT NULL CONSTRAINT [UQ_assessors_county] UNIQUE,
        [name] nvarchar(200) NOT NULL,
        [address] nvarchar(1000) NULL,
        [phone] nvarchar(100) NULL,
        [notes] nvarchar(1000) NULL
    );
END;

IF NOT EXISTS(SELECT 1 FROM [$(Schema)].[assessors])
BEGIN
    INSERT INTO [$(Schema)].[assessors] ([county],[name],[address],[phone],[notes]) VALUES
        (N'Arkansas', N'Marcia Theis', N'101 Court Square, DeWitt, 72042' + NCHAR(10) + N'312 S. College, Stuttgart, 72160', N'870-659-2105 / 870-673-6586', NULL),
        (N'Ashley', N'Beth Rush', N'205 E. Jefferson, Box #2, Hamburg, 71646' + NCHAR(10) + N'209 Main Street, Crossett, 71635', N'870-853-2060', NULL),
        (N'Baxter', N'Jayme Nicholson', N'6 E. 7th St., Mountain Home, 72653', N'870-425-3453', NULL),
        (N'Benton', N'Roderick Grieve', N'Bentonville: 2401 SW D. Street Ste 3, Bentonville, 72712' + NCHAR(10) + N'Gravette: 901 1st Ave SE (Hwy 59) Ste C, Gravette, 72736' + NCHAR(10) + N'Rogers: 2109 W Walnut St, Rogers, 72756' + NCHAR(10) + N'Siloam Springs: 707 Lincoln St, Siloam Springs, 72761', N'479-271-1033 (DFA) / 479-271-1037 (portal)', NULL),
        (N'Boone', N'Brandi Diffey', N'P.O. Box 2425, Harrison, 72602' + NCHAR(10) + N'220 N Arbor Drive, Harrison, 72601', N'870-741-3783', NULL),
        (N'Bradley', N'Stephanie Bigham', N'101 E. Cedar, Ste. 107/8, Warren, 71671', N'870-226-2211', N'Portal spells this Bingham (same person).'),
        (N'Calhoun', N'Teresa Carter', N'309 Main, Hampton, 71744', N'870-798-2740', N'Portal lists Teresa Ables instead — verify before relying on this for a real notification'),
        (N'Carroll', N'Jeannie Davidson', N'108 Spring St., Berryville, 72616' + NCHAR(10) + N'44 S. Main, Eureka Springs, 72632', N'870-423-6400 (Berryville) / 870-423-2388 (Eureka Springs)', NULL),
        (N'Chicot', N'Faye Tate', N'108 Main St., Lake Village, 71653', N'870-265-8025', N'Portal lists Barbara Townsend instead — verify before relying on this for a real notification'),
        (N'Clark', N'Mona Vance', N'401 Clay St., Arkadelphia, 71923', N'870-246-4431', N'Portal lists Kasey Summerville instead — verify before relying on this for a real notification'),
        (N'Clay', N'Tracy Gurley', N'151 S. Second Ave., Piggott, 72454' + NCHAR(10) + N'800 West Second, Corning, 72422', N'870-598-3870 (Piggott) / 870-857-3133 (Corning)', NULL),
        (N'Cleburne', N'Rachelle Miller', N'301 W. Main St., Heber Springs, 72543', N'501-362-8147', N'Portal lists Judy Land instead — verify before relying on this for a real notification'),
        (N'Cleveland', N'Barbara Reaves', N'P.O. Box 391, Rison, 71665' + NCHAR(10) + N'20 Magnolia St., Rison, 71665', N'870-325-6695', N'Portal spells this Reeves (same person).'),
        (N'Columbia', N'Shannon Hair', N'101 S. Court Square, Magnolia, 71753', N'870-234-4380', N'Portal lists Sandra Cawyer instead — verify before relying on this for a real notification'),
        (N'Conway', N'Mark Stobaugh', N'117 S. Moose St., Morrilton, 72110', N'501-354-9622', NULL),
        (N'Craighead', N'Hannah Towell', N'511 S. Union St., Ste. 130, Jonesboro, 72401', N'870-933-4572 (DFA) / 870-933-4570 (portal)', NULL),
        (N'Crawford', N'Sandra Heiner', N'300 Main St., Room 8, Van Buren, 72956', N'479-474-1751', NULL),
        (N'Crittenden', N'Kimberly Hollowell', N'250 Pine St., Ste. 1, Marion, 72364', N'870-739-3606', NULL),
        (N'Cross', N'Sherri Williams', N'705 E. Union, Room 5, Wynne, 72396', N'870-238-5715', NULL),
        (N'Dallas', N'Vanessa Pierce', N'206 W. 3rd St., Fordyce, 71742', N'870-352-7983', N'Portal lists Donna Jones instead — verify before relying on this for a real notification'),
        (N'Desha', N'Jessica Ferguson', N'P.O. Box 366, Arkansas City, 71630' + NCHAR(10) + N'604 President Street, Arkansas City, 71630', N'870-222-0927', NULL),
        (N'Drew', N'Cheri Adcock', N'210 S. Main St., Monticello, 71655', N'870-460-6240', NULL),
        (N'Faulkner', N'Krissy Lewis', N'806 Faulkner St., Conway, 72034', N'501-450-4905 ext. 300', NULL),
        (N'Franklin', N'Rose Mckinnon', N'219 W. Main St., Ozark, 72949', N'479-667-2415', N'Portal lists Cathy Bennett instead — verify before relying on this for a real notification'),
        (N'Fulton', N'Cari Long', N'P.O. Box 586, Salem, 72576', N'870-895-3592', N'Portal lists Brad Schaufler instead — verify before relying on this for a real notification'),
        (N'Garland', N'Shannon Sharp', N'200 Woodbine, Ste. 123, Hot Springs, 71901', N'501-622-3730', NULL),
        (N'Grant', N'Kristy Pruitt', N'101 N. Rose St., Room 102, Sheridan, 72150', N'870-942-3711', NULL),
        (N'Greene', N'Ashley Reynolds', N'320 West Court Street, Paragould, 72450', N'870-239-6303', N'Portal lists Jane Wheeler Moudy instead — verify before relying on this for a real notification'),
        (N'Hempstead', N'Renee Gilbert', N'400 South Washington, Hope, 71802', N'870-777-6190', N'Portal lists Kim Smith instead — verify before relying on this for a real notification'),
        (N'Hot Spring', N'Blake Riggan', N'210 Locust St., Ste. 4, Malvern, 72104', N'501-332-2461', NULL),
        (N'Howard', N'Cindy Butler', N'421 North Main, Nashville, 71852', N'870-845-7511', N'Portal lists Debbie Teague instead — verify before relying on this for a real notification'),
        (N'Independence', N'Diane Tucker', N'110 Broad St., Batesville, 72501', N'870-793-8842', NULL),
        (N'Izard', N'Tammy Sanders', N'15 S Spring St Ste B, Melbourne, 72556' + NCHAR(10) + N'P.O. Box 131, Melbourne, 72556', N'870-368-7810', NULL),
        (N'Jackson', N'Diann Ballard', N'208 Main Street, Newport, 72112', N'870-523-7410', N'Portal lists Nora Gibson instead — verify before relying on this for a real notification'),
        (N'Jefferson', N'Gloria Tillman', N'101 E. Barraque St. #112, Pine Bluff, 71601', N'870-541-5334 (DFA) / 870-541-5344 (portal)', N'Portal lists Yvonne Humphrey instead — verify before relying on this for a real notification'),
        (N'Johnson', N'Rusty Hardgrave', N'108 S. Fulton, Clarksville, 72830' + NCHAR(10) + N'215 West Main Street, Clarksville, 72830', N'479-754-3863', N'Portal lists Jill Tate instead — verify before relying on this for a real notification'),
        (N'Lafayette', N'Bille Jo Pierson', N'1 Courthouse Square, Lewisville, 71845', N'870-921-4808', N'Portal lists Becky Barnes instead — verify before relying on this for a real notification'),
        (N'Lawrence', N'Becky Holder', N'P.O. Box 187, Walnut Ridge, 72476' + NCHAR(10) + N'315 West Main St., Walnut Ridge, 72476', N'870-886-1135', NULL),
        (N'Lee', N'Becky Hogan', N'15 E. Chestnut St. #1, Marianna, 72360', N'870-295-7750', NULL),
        (N'Lincoln', N'Amy Harrison', N'300 S. Drew St., Room 106, Star City, 71667', N'870-628-4401', NULL),
        (N'Little River', N'Allie Rosenbaum', N'351 N. Second, Ste. 3, Ashdown, 71822', N'870-898-7204', NULL),
        (N'Logan', N'Shannon Tucker', N'25 W. Walnut, Room 12, Paris, 72855', N'479-963-2716', N'Portal lists Shannon Cotton instead — verify before relying on this for a real notification'),
        (N'Lonoke', N'Donna Pederson', N'212 N. Center, Lonoke, 72086' + NCHAR(10) + N'1604 S. Pine St., Suite E, Cabot, 72023', N'501-676-6938', N'Portal lists Jack McNally instead — verify before relying on this for a real notification'),
        (N'Madison', N'Christal Ogden', N'P.O. Box 334, Huntsville, 72740', N'479-738-2325', N'Portal lists Will Jones instead — verify before relying on this for a real notification'),
        (N'Marion', N'Tonya Eppes', N'P.O. Box 532, Yellville, 72687' + NCHAR(10) + N'300 E. Old Main St., Yellville, 72687', N'870-449-4113', NULL),
        (N'Miller', N'Joyce Dennington', N'400 Laurel St., Ste. 100, Texarkana, 71854', N'870-774-1502', NULL),
        (N'Mississippi', N'Brannah Bibbs', N'200 West Walnut, Ste. 101, Blytheville, 72315', N'870-763-6860', N'Portal lists Harley Bradley instead — verify before relying on this for a real notification'),
        (N'Monroe', N'Stacey Wilkerson', N'123 Madison St., Clarendon, 72029', N'870-747-3847', N'Portal lists Renee Neal instead — verify before relying on this for a real notification'),
        (N'Montgomery', N'Tammy McCarter', N'105 Hwy 270 E., Ste. 8, Mount Ida, 71957', N'870-867-3271', NULL),
        (N'Nevada', N'Pam Box', N'215 E. Second St. South, Ste. 106, Prescott, 71857', N'870-887-3410', NULL),
        (N'Newton', N'Stephen Willis', N'P.O. Box 45, Jasper, 72641' + NCHAR(10) + N'101 Court Street, Jasper, 72641', N'870-446-2438 (DFA) / 870-446-2937 (portal)', NULL),
        (N'Ouachita', N'Tonya McKenzie', N'145 Jefferson St. SW, Camden, 71701', N'870-837-2240', N'Portal lists Debbie Lambert instead — verify before relying on this for a real notification'),
        (N'Perry', N'Amanda Hawkins', N'P.O. Box 6, Perryville, 72126' + NCHAR(10) + N'310 West Main St. Ste. 101, Perryville, 72126', N'501-889-2865', NULL),
        (N'Phillips', N'Jerome Turner', N'620 Cherry St., Ste. 100, Helena, 72342', N'870-338-5535', NULL),
        (N'Pike', N'Staci Stewart', N'P.O. Box 356, Murfreesboro, 71958', N'870-285-3316', N'Portal lists Beckie Alden instead — verify before relying on this for a real notification'),
        (N'Poinsett', N'Josh Bradley', N'401 Market St., Harrisburg, 72432' + NCHAR(10) + N'P.O. Box 543, Harrisburg, 72432', N'870-578-4435 (DFA) / 870-578-0617 (portal)', NULL),
        (N'Polk', N'Jovan Thomas', N'507 Church Ave., Mena, 71953', N'479-394-8121 (DFA) / 479-394-8157 (portal)', NULL),
        (N'Pope', N'Dana Baker', N'100 W. Main St., Russellville, 72801', N'479-968-7418', NULL),
        (N'Prairie', N'Karan Skarda', N'200 Courthouse Square, Des Arc, 72040', N'870-256-4692', N'Portal lists Jeannie Lott instead — verify before relying on this for a real notification'),
        (N'Pulaski', N'Janet Troutman-Ward', N'201 S. Broadway, Ste. 310, Little Rock, 72201', N'501-340-6170', NULL),
        (N'Randolph', N'Krissy Massey', N'107 W. Broadway St., Ste. I, Pocahontas, 72455', N'870-892-3200', N'Portal lists Stacy M. Ingram instead — verify before relying on this for a real notification'),
        (N'Saline', N'Bob Ramsey', N'215 N. Main, Ste. 7, Benton, 72015', N'501-303-5622/5623', NULL),
        (N'Scott', N'Kendall Fowler', N'190 W. 1st St., Box 12, Waldron, 72958', N'479-637-2666', N'Portal lists Terri Churchill instead — verify before relying on this for a real notification'),
        (N'Searcy', N'Randy Crumley', N'P.O. Box 1335, Marshall, 72650' + NCHAR(10) + N'200 North Highway 27, Marshall, 72650', N'870-448-2464', N'Portal.arkansas.gov did not list an assessor name for this county — DFA name used, unconfirmed by a second source.'),
        (N'Sebastian', N'Zach Johnson', N'Fort Smith: 35 South 6th St. Room 105, Fort Smith, 72901' + NCHAR(10) + N'Greenwood: 301 E. Center, Room 113, Greenwood' + NCHAR(10) + N'Phoenix Ave East: 6515 Phoenix Avenue, Fort Smith, 72901', N'479-784-1516 (DFA) / 479-783-8948 (portal)', NULL),
        (N'Sevier', N'Sheila Ridley', N'115 N. 3rd St., Ste. 117, DeQueen, 71832', N'870-584-3182', NULL),
        (N'Sharp', N'Kathy Nix', N'P.O. Box 101, Ash Flat, 72513' + NCHAR(10) + N'718 Ash Flat Drive, Ash Flat, 72513', N'870-994-7327 (DFA) / 870-994-7328 (portal)', NULL),
        (N'St. Francis', N'Ginadell Adams', N'313 S. Izard, Ste. 7, Forrest City, 72335', N'870-261-1710', N'Portal lists Craig Jones instead — verify before relying on this for a real notification'),
        (N'Stone', N'Heather Stevens', N'108 W. Washington St., Mountain View, 72560', N'870-269-5521 (DFA) / 870-269-3524 (portal)', NULL),
        (N'Union', N'Michelle Barksdale', N'101 N. Washington St., Ste. 107, El Dorado, 71730', N'870-864-1920', N'Portal lists Vickie Deaton instead — verify before relying on this for a real notification'),
        (N'Van Buren', N'Emma Smiley', N'1414 Hwy 65 S., Ste. 117, Clinton, 72031', N'501-745-2464', NULL),
        (N'Washington', N'Russell Hill', N'280 N. College, Ste. 250, Fayetteville, 72758', N'479-444-1500 (DFA) / 479-444-1520 (portal)', N'Portal lists the zip as 72701 for this address; DFA lists 72758 — likely a typo on one source, unconfirmed which.'),
        (N'White', N'Gail Snyder', N'119 W. Arch St., Searcy, 72143', N'501-279-6208 (DFA) / 501-279-6205 (portal)', NULL),
        (N'Woodruff', N'Leslie Collins', N'500 N. Third St., Augusta, 72006', N'870-347-5151', NULL),
        (N'Yell', N'Sherry Hicks', N'P.O. Box 607, Danville, 72833' + NCHAR(10) + N'Main Street office, Danville, 72833', N'479-495-4857 / 479-229-2693', NULL);
END;
