SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

-- Circuit Clerk reference lookup - same architecture as 038_staff_directory.sql: a fixed,
-- independent reference table with zero auth/identity dependency, seeded with real data (source:
-- arcourts.gov's official Arkansas Judiciary circuit clerks directory) so the office never has to
-- hand-enter this per case. Not tied to cases directly - cases just look a row up by their
-- existing County column at read time. Idempotent CREATE TABLE + IF NOT EXISTS seed, matching
-- 038_staff_directory.sql's pattern exactly. Carroll County has two clerk offices (Berryville and
-- Eureka Springs, same clerk) - combined into one row with both addresses on separate lines in
-- [address], mirroring how case_defendants.address already handles multi-address free text. There
-- is no live SQL Server sandbox available here to exercise this against a real pilot instance -
-- same limitation already noted for every other migration in this repo - so this file has been
-- reviewed for consistency with its siblings but not executed live.

IF OBJECT_ID(N'$(Schema).circuit_clerks','U') IS NULL
BEGIN
    CREATE TABLE [$(Schema)].[circuit_clerks]
    (
        [id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_circuit_clerks] PRIMARY KEY,
        [county] nvarchar(100) NOT NULL CONSTRAINT [UQ_circuit_clerks_county] UNIQUE,
        [clerk_name] nvarchar(200) NOT NULL,
        [address] nvarchar(1000) NULL,
        [phone] nvarchar(100) NULL,
        [notes] nvarchar(1000) NULL
    );
END;

IF NOT EXISTS(SELECT 1 FROM [$(Schema)].[circuit_clerks])
BEGIN
    INSERT INTO [$(Schema)].[circuit_clerks] ([county],[clerk_name],[address],[phone]) VALUES
        (N'Arkansas', N'Sarah Merchant', N'302 S. College, Stuttgart, 72042', N'870-659-2098 ext. 1'),
        (N'Ashley', N'Vickie Stell', N'205 East Jefferson, #6, Hamburg, 71646', N'870-853-2030'),
        (N'Baxter', N'Canda Reese', N'#1 East Seventh, Suite 103, Mountain Home, 72653', N'870-425-3475'),
        (N'Benton', N'Brenda DeShields', N'102 NE "A" Street, Bentonville, 72712', N'479-271-1015'),
        (N'Boone', N'Judy Kay Harris', N'100 N. Main Street, #200, Harrison, 72601', N'870-741-5560 ext 1'),
        (N'Bradley', N'Cindy Wagnon', N'101 East Cedar, Warren, 71671', N'870-226-2272'),
        (N'Calhoun', N'Jeanie Smith', N'P.O. Box 1175, Hampton, 71744', N'870-798-2517'),
        (N'Carroll', N'Sara Huffman', N'210 W. Church Ave., Berryville, 72616' + NCHAR(10) + N'44 S. Main St., Eureka Springs, 72632', N'870-423-2422 (Berryville) / 479-253-8646 (Eureka Springs)'),
        (N'Chicot', N'Josephine Griffin', N'108 Main Street, Lake Village, 71653', N'870-265-8010'),
        (N'Clark', N'Brian Daniel', N'401 Clay Street, Arkadelphia, 71923', N'870-246-4281'),
        (N'Clay', N'Angela Self', N'151 S. Second Street, Piggott, 72454', N'870-598-2524'),
        (N'Cleburne', N'Heather Smith', N'301 W. Main St, Heber Springs, 72543', N'501-362-8149'),
        (N'Cleveland', N'Brandy Herring', N'P.O. Box 368, Rison, 71665', N'870-325-6521'),
        (N'Columbia', N'Lisa C. Lewis', N'1 Court Square, Magnolia, 71753', N'870-235-3700'),
        (N'Conway', N'Darlene Massingill', N'115 S. Moose Street, Morrilton, 72110', N'501-354-9617'),
        (N'Craighead', N'David Vaughn', N'511 S. Main Street, Jonesboro, 72401', N'870-933-4530'),
        (N'Crawford', N'Sharon Blount-Baker', N'317 Main Street, Van Buren, 72956', N'479-474-1821'),
        (N'Crittenden', N'Terry Hawkins', N'100 Court Square, Marion, 72364', N'870-739-3248'),
        (N'Cross', N'Rhonda Sullivan', N'705 East Union, Wynne, 72396', N'870-238-5720'),
        (N'Dallas', N'Dori Keeton', N'Third and Oak St., Fordyce, 71742', N'870-352-2307'),
        (N'Desha', N'Kristin Christmas', N'P.O. Box 309, Arkansas City, 71630', N'870-222-0930'),
        (N'Drew', N'Beverly Burks', N'210 South Main, Monticello, 71655', N'870-460-6250'),
        (N'Faulkner', N'Nancy Eastham', N'P.O. Box 9, Conway, 72034', N'501-450-4911'),
        (N'Franklin', N'Janice King', N'P.O. Box 1112, Ozark, 72949', N'479-965-7332'),
        (N'Fulton', N'Vickie Bishop', N'P.O. Box 219, Salem, 72576', N'870-895-3310'),
        (N'Garland', N'Kristie Womble-Hughes', N'501 Ouachita, Suite No. 207, Hot Springs, 71901', N'501-622-3630'),
        (N'Grant', N'Geral Harrison', N'103 W. Center, Room 106, Sheridan, 72150', N'870-942-2631'),
        (N'Greene', N'Lesa Gramling', N'320 West Court Street, Paragould, 72450', N'870-239-6330'),
        (N'Hempstead', N'Gail Wolfenbarger', N'200 E 3rd Street, Hope, 71801', N'870-777-2384'),
        (N'Hot Spring', N'Teresa Pilcher', N'210 Locust Street, Malvern, 72104', N'501-332-2281'),
        (N'Howard', N'Angie Lewis', N'421 N. Main St., Nashville, 71852', N'870-845-7500 Ext. 5'),
        (N'Independence', N'Greg Wallis', N'P.O. Box 2155, Batesville, 72501', N'870-793-8833'),
        (N'Izard', N'Joe M. Cooper', N'P.O. Box 95, Melbourne, 72556', N'870-368-4316'),
        (N'Jackson', N'Barbara Metzger-Hackney', N'208 Main Street, Newport, 72112', N'870-523-7423'),
        (N'Jefferson', N'Flora Cook-Bishop', N'101 East Barraque Street, Pine Bluff, 71601', N'870-541-5306'),
        (N'Johnson', N'Monica King', N'P.O. Box 189, Clarksville, 72830', N'479-754-2977'),
        (N'Lafayette', N'Dana Phillips', N'3rd & Spruce Streets, Lewisville, 71845', N'870-921-4878'),
        (N'Lawrence', N'Michelle Evans', N'315 West Main, Walnut Ridge, 72476', N'870-886-1112'),
        (N'Lee', N'Millie A. Hill', N'15 East Chestnut Street, Room 2, Marianna, 72360', N'870-295-7710'),
        (N'Lincoln', N'Cindy Glover', N'300 S. Drew Street, Star City, 71667', N'870-628-3154'),
        (N'Little River', N'Lauren Abney', N'351 North Second, Ashdown, 71822', N'870-898-7280'),
        (N'Logan', N'April Hice', N'25 W. Walnut, Paris, 72855', N'479-963-2164'),
        (N'Lonoke', N'Deborah Oglesby', N'P.O. Box 870, Lonoke, 72086', N'501-676-2316'),
        (N'Madison', N'Tiffany McDaniel', N'P.O. Box 626, Huntsville, 72740', N'479-738-2215'),
        (N'Marion', N'Dawn Moffett', N'P.O. Box 385, Yellville, 72687', N'870-449-6226'),
        (N'Miller', N'Penny Kilcrease', N'400 Laurel Street, Room 109, Texarkana, 71854', N'870-774-4501'),
        (N'Mississippi', N'Leslie Mullins Mason', N'206 North 2nd Street, P.O. Box 1498, Blytheville, 72315', N'870-762-2332'),
        (N'Monroe', N'Alice Smith', N'123 Madison Street, Clarendon, 72029', N'870-747-3615'),
        (N'Montgomery', N'Regina Powell', N'105 Highway 270 E. #10, Mount Ida, 71957', N'870-867-3521'),
        (N'Nevada', N'Rita Reyenga', N'215 East 2nd Street South, Prescott, 71857', N'870-887-2511'),
        (N'Newton', N'Donnie Davis', N'P.O. Box 410, Jasper, 72641', N'870-446-5125'),
        (N'Ouachita', N'Gladys Nettles', N'145 Jefferson Street, Camden, 71701', N'870-837-2230'),
        (N'Perry', N'Renee Rainey', N'P.O. Box 358, Perryville, 72126', N'501-889-5126'),
        (N'Phillips', N'Tameka Franklin', N'620 Cherry Street, Helena, 72342', N'870-338-5515'),
        (N'Pike', N'Sabrina Williams', N'P.O. Box 219, Murfreesboro, 71958', N'870-285-2231'),
        (N'Poinsett', N'Misty Russell', N'401 Market Street, Harrisburg, 72432', N'870-578-4420'),
        (N'Polk', N'Michelle Schnell', N'507 Church Avenue, Mena, 71953', N'479-394-8100'),
        (N'Pope', N'Rachel Oertling', N'100 West Main, Russellville, 72801', N'479-968-7499'),
        (N'Prairie', N'Gaylon Hale', N'200 Courthouse Square, Ste 104, Des Arc, 72040', N'870-256-4434'),
        (N'Pulaski', N'Terri Hollingsworth', N'401 West Markham, Suite 100, Little Rock, 72201', N'501-340-8500'),
        (N'Randolph', N'Debbie Wise', N'107 West Broadway, Pocahontas, 72455', N'870-892-5522'),
        (N'Saline', N'Myka Bono Sample', N'200 North Main, Benton, 72015', N'501-303-5615'),
        (N'Scott', N'Brianna Freeman', N'190 West First Street, Box 10, Waldron, 72958', N'479-637-2642'),
        (N'Searcy', N'Jeffrey Cotton', N'P.O. Box 998, Marshall, 72650', N'870-448-3807'),
        (N'Sebastian', N'Susie Hassett', N'P. O. Box 1179, Fort Smith, 72901', N'479-782-1046'),
        (N'Sevier', N'Kathy Smith', N'115 North 3rd, DeQueen, 71832', N'870-584-3055'),
        (N'Sharp', N'Alisa Black', N'P.O. Box 307, Ash Flat, 72513', N'870-994-7361'),
        (N'St. Francis', N'Alan T. Smith', N'313 South Izard, Forrest City, 72335', N'870-261-1715'),
        (N'Stone', N'Angie Hudspeth-Wade', N'107 West Main, Suite D, Mountain View, 72560', N'870-269-3271'),
        (N'Union', N'Cheryl Wilson', N'101 N. Washington, Suite 201, El Dorado, 71730', N'870-864-1940'),
        (N'Van Buren', N'Debbie Gray', N'273 Main Street, Suite 2, Clinton, 72031', N'501-745-4140'),
        (N'Washington', N'Kyle Sylvester', N'280 N College Ave., #302, Fayetteville, 72701', N'479-444-1538'),
        (N'White', N'Sara Brown', N'300 N. Spruce, Searcy, 72143', N'501-279-6203'),
        (N'Woodruff', N'Lori Grisham', N'500 N. Third Street, Augusta, 72006', N'870-347-2391'),
        (N'Yell', N'Anna Ward', N'P.O. Box 219, Danville, 72833', N'479-495-4850');
END;
