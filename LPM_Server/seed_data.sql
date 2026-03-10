-- ============================================================
-- PART 2: Add 50 new Persons
-- ============================================================
INSERT INTO Persons (FirstName, LastName, Phone, Email, DateOfBirth, Gender, Org, Referral, IsActive) VALUES
('Alon', 'Koren', '050-1111001', 'alon.k@gmail.com', '1990-02-14', 'Male', NULL, 'Friend', 1),
('Maya', 'Lavi', '052-1111002', 'maya.l@gmail.com', '1993-05-22', 'Female', 'Tel Aviv', NULL, 1),
('Oren', 'Dagan', '053-1111003', 'oren.d@gmail.com', '1987-08-03', 'Male', 'Haifa', 'Social Networks', 1),
('Liora', 'Malka', '054-1111004', 'liora.m@gmail.com', '1995-11-17', 'Female', NULL, 'Friend', 1),
('Itai', 'Sharabi', '050-1111005', 'itai.s@gmail.com', '1982-04-09', 'Male', 'Jerusalem', NULL, 1),
('Neta', 'Amir', '052-1111006', 'neta.a@gmail.com', '1997-01-28', 'Female', NULL, 'Social Networks', 1),
('Gal', 'Zohar', '053-1111007', 'gal.z@gmail.com', '1989-06-15', 'Male', 'Haifa', 'Friend', 1),
('Rotem', 'Kafri', '054-1111008', 'rotem.k@gmail.com', '1994-09-30', 'Female', 'Riga', NULL, 1),
('Yonatan', 'Stern', '050-1111009', 'yonatan.s@gmail.com', '1986-12-05', 'Male', NULL, 'Friend', 1),
('Michal', 'Elias', '052-1111010', 'michal.e@gmail.com', '1991-03-20', 'Female', 'Tel Aviv', 'Social Networks', 1),
('Erez', 'Naor', '053-1111011', 'erez.n@gmail.com', '1984-07-11', 'Male', 'Haifa', NULL, 1),
('Shani', 'Raz', '054-1111012', 'shani.r@gmail.com', '1996-10-25', 'Female', NULL, 'Friend', 1),
('Noam', 'Harel', '050-1111013', 'noam.h@gmail.com', '1988-01-08', 'Male', 'Jerusalem', NULL, 1),
('Inbar', 'Tsur', '052-1111014', 'inbar.t@gmail.com', '1993-04-14', 'Female', 'Haifa', 'Social Networks', 1),
('Avi', 'Gross', '053-1111015', 'avi.g@gmail.com', '1981-08-22', 'Male', NULL, 'Friend', 1),
('Keren', 'Wolf', '054-1111016', 'keren.w@gmail.com', '1992-11-06', 'Female', 'Tel Aviv', NULL, 1),
('Lior', 'Ashkenazi', '050-1111017', 'lior.a@gmail.com', '1985-02-19', 'Male', NULL, 'Social Networks', 1),
('Talia', 'Barak', '052-1111018', 'talia.b@gmail.com', '1997-05-31', 'Female', 'Riga', 'Friend', 1),
('Omri', 'Hen', '053-1111019', 'omri.h@gmail.com', '1983-09-13', 'Male', 'Haifa', NULL, 1),
('Efrat', 'Yosef', '054-1111020', 'efrat.y@gmail.com', '1990-12-28', 'Female', NULL, 'Social Networks', 1),
('Nadav', 'Rivlin', '050-1111021', 'nadav.r@gmail.com', '1986-03-07', 'Male', 'Jerusalem', 'Friend', 1),
('Ayelet', 'Shachar', '052-1111022', 'ayelet.s@gmail.com', '1994-06-16', 'Female', 'Tel Aviv', NULL, 1),
('Roi', 'Agmon', '053-1111023', 'roi.a@gmail.com', '1988-10-02', 'Male', 'Haifa', NULL, 1),
('Dana', 'Ofer', '054-1111024', 'dana.o@gmail.com', '1991-01-21', 'Female', NULL, 'Friend', 1),
('Yoav', 'Meir', '050-1111025', 'yoav.m@gmail.com', '1980-04-15', 'Male', NULL, 'Social Networks', 1),
('Sivan', 'Feldman', '052-1111026', 'sivan.f@gmail.com', '1995-07-09', 'Female', 'Riga', NULL, 1),
('Asaf', 'Peled', '053-1111027', 'asaf.p@gmail.com', '1987-11-24', 'Male', 'Haifa', 'Friend', 1),
('Mor', 'Golan', '054-1111028', 'mor.g@gmail.com', '1992-02-08', 'Female', 'Tel Aviv', NULL, 1),
('Ido', 'Carmel', '050-1111029', 'ido.c@gmail.com', '1984-05-19', 'Male', NULL, 'Social Networks', 1),
('Yarden', 'Atar', '052-1111030', 'yarden.a@gmail.com', '1996-08-30', 'Female', 'Haifa', 'Friend', 1),
('Gil', 'Rosenberg', '053-1111031', 'gil.r@gmail.com', '1981-01-12', 'Male', 'Jerusalem', NULL, 1),
('Sapir', 'Einav', '054-1111032', 'sapir.e@gmail.com', '1993-04-26', 'Female', NULL, 'Social Networks', 1),
('Ben', 'Nissim', '050-1111033', 'ben.n@gmail.com', '1989-07-05', 'Male', 'Haifa', NULL, 1),
('Ofir', 'Sade', '052-1111034', 'ofir.s@gmail.com', '1985-10-18', 'Female', 'Tel Aviv', 'Friend', 1),
('Amit', 'Yogev', '053-1111035', 'amit.y@gmail.com', '1990-01-30', 'Male', NULL, NULL, 1),
('Rona', 'Drori', '054-1111036', 'rona.d@gmail.com', '1994-04-14', 'Female', 'Riga', 'Social Networks', 1),
('Yuval', 'Paz', '050-1111037', 'yuval.p@gmail.com', '1983-08-22', 'Male', 'Haifa', 'Friend', 1),
('Meirav', 'Shalom', '052-1111038', 'meirav.s@gmail.com', '1991-11-05', 'Female', NULL, NULL, 1),
('Ohad', 'Tzion', '053-1111039', 'ohad.t@gmail.com', '1986-02-17', 'Male', 'Jerusalem', 'Social Networks', 1),
('Chen', 'Avital', '054-1111040', 'chen.a@gmail.com', '1997-06-29', 'Female', 'Tel Aviv', 'Friend', 1),
('Tom', 'Adi', '050-1111041', 'tom.adi@gmail.com', '1988-09-08', 'Male', NULL, NULL, 1),
('Stav', 'Ran', '052-1111042', 'stav.r@gmail.com', '1992-12-21', 'Female', 'Haifa', 'Social Networks', 1),
('Uri', 'Naaman', '053-1111043', 'uri.n@gmail.com', '1984-03-03', 'Male', NULL, 'Friend', 1),
('Hadar', 'Even', '054-1111044', 'hadar.e@gmail.com', '1995-06-15', 'Female', 'Riga', NULL, 1),
('Shaul', 'Bar-Lev', '050-1111045', 'shaul.b@gmail.com', '1979-10-27', 'Male', 'Jerusalem', NULL, 1),
('Naama', 'Gat', '052-1111046', 'naama.g@gmail.com', '1993-01-09', 'Female', 'Haifa', 'Friend', 1),
('Ariel', 'Dvir', '053-1111047', 'ariel.d@gmail.com', '1987-04-22', 'Male', NULL, 'Social Networks', 1),
('Lilach', 'Har-El', '054-1111048', 'lilach.h@gmail.com', '1990-07-06', 'Female', 'Tel Aviv', NULL, 1),
('Tzahi', 'Arbel', '050-1111049', 'tzahi.a@gmail.com', '1982-11-18', 'Male', 'Haifa', 'Friend', 1),
('Osnat', 'Geva', '052-1111050', 'osnat.g@gmail.com', '1996-02-01', 'Female', NULL, 'Social Networks', 1);

-- ============================================================
-- PART 3: Register all new persons as PCs
-- ============================================================
INSERT OR IGNORE INTO PCs (PcId) SELECT PersonId FROM Persons WHERE PersonId >= 54;
-- Also add missing existing persons as PCs
INSERT OR IGNORE INTO PCs (PcId) VALUES (37),(38),(39),(41),(42),(43),(44),(45),(48),(49),(50),(51);

-- ============================================================
-- PART 4: Fix purchases without payment methods
-- ============================================================
INSERT INTO PurchasePaymentMethods (PurchaseId, MethodType, Amount, PaymentDate, IsMoneyInBank, MoneyInBankDate)
SELECT p.PurchaseId, 'Cash',
       COALESCE((SELECT SUM(AmountPaid) FROM PurchaseItems WHERE PurchaseId=p.PurchaseId), 0),
       p.PurchaseDate, 1, p.PurchaseDate
FROM Purchases p
WHERE p.PurchaseId NOT IN (SELECT DISTINCT PurchaseId FROM PurchasePaymentMethods);

-- ============================================================
-- PART 5: Add more Courses
-- ============================================================
INSERT INTO Courses (Name) VALUES
('Life Repair'),
('Purification'),
('Student Hat'),
('Method One'),
('Ethics Specialist'),
('PRO TRs'),
('Upper Indoc TRs'),
('Book One Auditor'),
('Survival Rundown'),
('Basic Study Manual');

-- ============================================================
-- PART 6: Massive Sessions data (spread across last 8 weeks)
-- Auditors: 1(Tami),2(Genia),3(Eitan),5(Eyal),6(Aviv),7(Samai),8(Yaniv),34(Solo)
-- ============================================================

-- Week 1: Feb 5-11 (Thu-Wed)
INSERT INTO Sessions (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFreeSession, ChargeSeconds, ChargedRateCentsPerHour, VerifiedStatus, AuditorSalaryCentsPerHour) VALUES
(54,1,'2026-02-05',1,3600,300,0,3600,5000,'Approved',3000),
(55,1,'2026-02-05',2,5400,600,0,5400,5000,'Approved',3000),
(56,6,'2026-02-06',1,3600,300,0,3600,5000,'Approved',3000),
(57,6,'2026-02-06',2,7200,600,0,7200,5000,'Approved',3000),
(58,3,'2026-02-07',1,3600,300,0,3600,5000,'Approved',3000),
(59,3,'2026-02-08',1,5400,450,0,5400,5000,'Approved',3000),
(60,5,'2026-02-09',1,3600,300,0,3600,5000,'Approved',3000),
(61,5,'2026-02-09',2,3600,300,0,3600,5000,'Approved',3000),
(62,7,'2026-02-10',1,5400,450,0,5400,5000,'Approved',3000),
(63,7,'2026-02-10',2,3600,300,0,3600,5000,'Approved',3000),
(54,8,'2026-02-11',1,3600,300,0,3600,5000,'Approved',3000),
(64,1,'2026-02-11',1,7200,600,0,7200,5000,'Approved',3000);

-- Week 2: Feb 12-18
INSERT INTO Sessions (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFreeSession, ChargeSeconds, ChargedRateCentsPerHour, VerifiedStatus, AuditorSalaryCentsPerHour) VALUES
(65,1,'2026-02-12',1,3600,300,0,3600,5000,'Approved',3000),
(66,1,'2026-02-13',1,5400,450,0,5400,5000,'Approved',3000),
(67,6,'2026-02-12',1,3600,300,0,3600,5000,'Approved',3000),
(68,6,'2026-02-13',1,7200,600,0,7200,5000,'Approved',3000),
(69,6,'2026-02-14',1,3600,300,0,3600,5000,'Approved',3000),
(70,3,'2026-02-15',1,5400,450,0,5400,5000,'Approved',3000),
(71,3,'2026-02-15',2,3600,300,0,3600,5000,'Approved',3000),
(72,5,'2026-02-16',1,3600,300,0,3600,5000,'Approved',3000),
(73,7,'2026-02-17',1,5400,450,0,5400,5000,'Approved',3000),
(74,7,'2026-02-18',1,3600,300,0,3600,5000,'Approved',3000),
(54,1,'2026-02-14',1,3600,300,0,3600,5000,'Approved',3000),
(55,6,'2026-02-15',1,5400,450,0,5400,5000,'Approved',3000),
(75,8,'2026-02-16',1,7200,600,0,7200,5000,'Approved',3000),
(76,8,'2026-02-17',1,3600,300,0,3600,5000,'Approved',3000);

-- Week 3: Feb 19-25
INSERT INTO Sessions (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFreeSession, ChargeSeconds, ChargedRateCentsPerHour, VerifiedStatus, AuditorSalaryCentsPerHour) VALUES
(77,1,'2026-02-19',1,3600,300,0,3600,5000,'Approved',3000),
(78,1,'2026-02-20',1,5400,450,0,5400,5000,'Approved',3000),
(79,6,'2026-02-19',1,7200,600,0,7200,5000,'Approved',3000),
(80,6,'2026-02-20',1,3600,300,0,3600,5000,'Approved',3000),
(81,6,'2026-02-21',1,5400,450,0,5400,5000,'Approved',3000),
(82,3,'2026-02-22',1,3600,300,0,3600,5000,'Approved',3000),
(83,5,'2026-02-23',1,5400,450,0,5400,5000,'Approved',3000),
(84,7,'2026-02-24',1,3600,300,0,3600,5000,'Approved',3000),
(85,7,'2026-02-25',1,7200,600,0,7200,5000,'Approved',3000),
(56,1,'2026-02-21',1,3600,300,0,3600,5000,'Approved',3000),
(57,6,'2026-02-22',1,5400,450,0,5400,5000,'Approved',3000),
(58,3,'2026-02-23',1,3600,300,0,3600,5000,'Approved',3000),
(86,8,'2026-02-24',1,5400,450,0,5400,5000,'Approved',3000),
(87,8,'2026-02-25',1,3600,300,0,3600,5000,'Approved',3000);

-- Week 4: Feb 26 - Mar 4
INSERT INTO Sessions (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFreeSession, ChargeSeconds, ChargedRateCentsPerHour, VerifiedStatus, AuditorSalaryCentsPerHour) VALUES
(88,1,'2026-02-26',1,3600,300,0,3600,5000,'Approved',3000),
(89,1,'2026-02-27',1,5400,450,0,5400,5000,'Approved',3000),
(90,6,'2026-02-26',1,3600,300,0,3600,5000,'Approved',3000),
(91,6,'2026-02-27',1,7200,600,0,7200,5000,'Approved',3000),
(92,6,'2026-02-28',1,3600,300,0,3600,5000,'Approved',3000),
(93,3,'2026-03-01',1,5400,450,0,5400,5000,'Approved',3000),
(94,5,'2026-03-02',1,3600,300,0,3600,5000,'Approved',3000),
(95,7,'2026-03-03',1,5400,450,0,5400,5000,'Approved',3000),
(96,7,'2026-03-04',1,3600,300,0,3600,5000,'Approved',3000),
(54,1,'2026-02-28',1,3600,300,0,3600,5000,'Approved',3000),
(59,6,'2026-03-01',1,5400,450,0,5400,5000,'Approved',3000),
(60,3,'2026-03-02',1,3600,300,0,3600,5000,'Approved',3000),
(97,8,'2026-03-03',1,7200,600,0,7200,5000,'Approved',3000),
(98,8,'2026-03-04',1,3600,300,0,3600,5000,'Approved',3000);

-- Week 5: Mar 5-11 (current week area)
INSERT INTO Sessions (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFreeSession, ChargeSeconds, ChargedRateCentsPerHour, VerifiedStatus, AuditorSalaryCentsPerHour) VALUES
(99,1,'2026-03-05',1,3600,300,0,3600,5000,'Approved',3000),
(100,1,'2026-03-06',1,5400,450,0,5400,5000,'Approved',3000),
(101,6,'2026-03-05',1,3600,300,0,3600,5000,'Approved',3000),
(102,6,'2026-03-06',1,7200,600,0,7200,5000,'Approved',3000),
(103,6,'2026-03-07',1,3600,300,0,3600,5000,'Approved',3000),
(54,3,'2026-03-08',1,5400,450,0,5400,5000,'Draft',3000),
(55,5,'2026-03-09',1,3600,300,0,3600,5000,'Draft',3000),
(56,7,'2026-03-10',1,5400,450,0,5400,5000,'Draft',3000),
(57,7,'2026-03-10',2,3600,300,0,3600,5000,'Draft',3000),
(61,1,'2026-03-07',1,3600,300,0,3600,5000,'Approved',3000),
(62,6,'2026-03-08',1,5400,450,0,5400,5000,'Draft',3000),
(63,3,'2026-03-09',1,3600,300,0,3600,5000,'Draft',3000),
(64,8,'2026-03-10',1,7200,600,1,0,0,'Draft',3000),
(65,8,'2026-03-10',2,3600,300,0,3600,5000,'Draft',3000);

-- Solo sessions (PcId = AuditorId for solo)
INSERT INTO Sessions (PcId, AuditorId, SessionDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFreeSession, ChargeSeconds, ChargedRateCentsPerHour, VerifiedStatus, AuditorSalaryCentsPerHour) VALUES
(34,34,'2026-02-06',1,3600,300,0,3600,5000,'Approved',3000),
(34,34,'2026-02-13',1,5400,450,0,5400,5000,'Approved',3000),
(34,34,'2026-02-20',1,3600,300,0,3600,5000,'Approved',3000),
(34,34,'2026-02-27',1,3600,300,0,3600,5000,'Approved',3000),
(34,34,'2026-03-06',1,5400,450,0,5400,5000,'Draft',3000),
(1,1,'2026-02-08',1,3600,300,0,3600,5000,'Approved',3000),
(1,1,'2026-02-15',1,5400,450,0,5400,5000,'Approved',3000),
(1,1,'2026-02-22',1,3600,300,0,3600,5000,'Approved',3000),
(6,6,'2026-02-10',1,7200,600,0,7200,5000,'Approved',3000),
(6,6,'2026-02-17',1,3600,300,0,3600,5000,'Approved',3000),
(6,6,'2026-02-24',1,5400,450,0,5400,5000,'Approved',3000),
(6,6,'2026-03-03',1,3600,300,0,3600,5000,'Approved',3000);

-- ============================================================
-- PART 7: CsReviews for some sessions
-- CaseSupervisors: 1(Tami), 4(Carmela), 6(Aviv)
-- ============================================================
INSERT INTO CsReviews (SessionId, CsId, ReviewLengthSeconds, ReviewedAt, Status, CsSalaryCentsPerHour)
SELECT s.SessionId,
  CASE WHEN s.SessionId % 3 = 0 THEN 1 WHEN s.SessionId % 3 = 1 THEN 4 ELSE 6 END,
  1800,
  datetime(s.SessionDate, '+1 day'),
  CASE WHEN s.VerifiedStatus = 'Approved' THEN 'Approved' ELSE 'Draft' END,
  2500
FROM Sessions s
WHERE s.SessionId NOT IN (SELECT SessionId FROM CsReviews)
  AND s.AuditorId != s.PcId  -- not solo
  AND s.SessionId % 4 != 0   -- ~75% get reviews
ORDER BY s.SessionId
LIMIT 80;

-- ============================================================
-- PART 8: CsWorkLog entries
-- ============================================================
INSERT INTO CsWorkLog (CsId, PcId, WorkDate, LengthSeconds, Notes) VALUES
(1, 54, '2026-02-06', 1800, 'Case review and planning'),
(1, 55, '2026-02-07', 2400, 'Progress evaluation'),
(1, 56, '2026-02-10', 1200, 'Initial assessment'),
(4, 57, '2026-02-12', 1800, 'Weekly check-in'),
(4, 58, '2026-02-14', 3600, 'Comprehensive review'),
(6, 59, '2026-02-16', 1800, 'Case notes update'),
(6, 60, '2026-02-18', 2400, 'Planning session'),
(1, 61, '2026-02-20', 1800, 'Follow-up review'),
(1, 62, '2026-02-22', 1200, 'Brief check'),
(4, 63, '2026-02-24', 3600, 'Full case review'),
(6, 64, '2026-02-26', 1800, 'Progress notes'),
(6, 65, '2026-02-28', 2400, 'Audit prep'),
(1, 66, '2026-03-02', 1800, 'Weekly planning'),
(4, 67, '2026-03-04', 1200, 'Quick review'),
(6, 68, '2026-03-06', 3600, 'Detailed assessment'),
(1, 69, '2026-03-08', 1800, 'Case supervision'),
(4, 70, '2026-03-09', 2400, 'Session debrief'),
(6, 71, '2026-03-10', 1800, 'Current status review');

-- ============================================================
-- PART 9: Purchases and PurchaseItems for new PCs
-- ============================================================
INSERT INTO Purchases (PcId, PurchaseDate, Notes, ApprovedStatus, CreatedByPersonId, IsDeleted) VALUES
(54, '2026-02-05', 'Initial package purchase', 'Approved', 8, 0),
(55, '2026-02-05', 'Course enrollment', 'Approved', 8, 0),
(56, '2026-02-06', 'Auditing hours', 'Approved', 8, 0),
(57, '2026-02-06', 'Combined package', 'Approved', 8, 0),
(58, '2026-02-07', 'Auditing hours', 'Approved', 8, 0),
(59, '2026-02-08', 'Course + auditing', 'Approved', 8, 0),
(60, '2026-02-09', 'Standard package', 'Pending', 8, 0),
(61, '2026-02-09', 'Auditing hours', 'Pending', 8, 0),
(62, '2026-02-10', 'Course enrollment', 'Approved', 8, 0),
(63, '2026-02-10', 'Big package', 'Approved', 8, 0),
(64, '2026-02-11', 'Initial purchase', 'Approved', 8, 0),
(65, '2026-02-12', 'Auditing hours', 'Approved', 8, 0),
(66, '2026-02-13', 'Course package', 'Pending', 8, 0),
(67, '2026-02-14', 'Auditing + course', 'Approved', 8, 0),
(68, '2026-02-15', 'Renewal', 'Approved', 8, 0),
(69, '2026-03-01', 'March package', 'Approved', 8, 0),
(70, '2026-03-02', 'Course registration', 'Pending', 8, 0),
(71, '2026-03-05', 'New client intro', 'Approved', 8, 0),
(72, '2026-03-07', 'Standard hours', 'Pending', 8, 0),
(73, '2026-03-09', 'Full program', 'Pending', 8, 0);

-- PurchaseItems for the new purchases
INSERT INTO PurchaseItems (PurchaseId, ItemType, CourseId, HoursBought, AmountPaid, RegistrarId, ReferralId) VALUES
-- Purchase for PC 54
((SELECT PurchaseId FROM Purchases WHERE PcId=54 AND PurchaseDate='2026-02-05' LIMIT 1), 'Auditing', NULL, 20, 50000, 8, NULL),
-- Purchase for PC 55
((SELECT PurchaseId FROM Purchases WHERE PcId=55 LIMIT 1), 'Course', 1, 0, 15000, 8, NULL),
((SELECT PurchaseId FROM Purchases WHERE PcId=55 LIMIT 1), 'Auditing', NULL, 10, 25000, 8, NULL),
-- Purchase for PC 56
((SELECT PurchaseId FROM Purchases WHERE PcId=56 LIMIT 1), 'Auditing', NULL, 15, 37500, 8, NULL),
-- Purchase for PC 57
((SELECT PurchaseId FROM Purchases WHERE PcId=57 LIMIT 1), 'Auditing', NULL, 25, 62500, 8, NULL),
((SELECT PurchaseId FROM Purchases WHERE PcId=57 LIMIT 1), 'Course', 5, 0, 20000, 8, NULL),
-- Purchase for PC 58
((SELECT PurchaseId FROM Purchases WHERE PcId=58 LIMIT 1), 'Auditing', NULL, 12, 30000, 8, NULL),
-- Purchase for PC 59
((SELECT PurchaseId FROM Purchases WHERE PcId=59 LIMIT 1), 'Course', 6, 0, 18000, 8, NULL),
((SELECT PurchaseId FROM Purchases WHERE PcId=59 LIMIT 1), 'Auditing', NULL, 8, 20000, 8, NULL),
-- Purchase for PC 60
((SELECT PurchaseId FROM Purchases WHERE PcId=60 LIMIT 1), 'Auditing', NULL, 15, 37500, 8, NULL),
-- Purchase for PC 61
((SELECT PurchaseId FROM Purchases WHERE PcId=61 LIMIT 1), 'Auditing', NULL, 10, 25000, 8, NULL),
-- Purchase for PC 62
((SELECT PurchaseId FROM Purchases WHERE PcId=62 LIMIT 1), 'Course', 7, 0, 22000, 8, NULL),
-- Purchase for PC 63
((SELECT PurchaseId FROM Purchases WHERE PcId=63 LIMIT 1), 'Auditing', NULL, 50, 125000, 8, NULL),
((SELECT PurchaseId FROM Purchases WHERE PcId=63 LIMIT 1), 'Course', 2, 0, 25000, 8, NULL),
-- Purchase for PC 64
((SELECT PurchaseId FROM Purchases WHERE PcId=64 LIMIT 1), 'Auditing', NULL, 20, 50000, 8, NULL),
-- Purchase for PC 65
((SELECT PurchaseId FROM Purchases WHERE PcId=65 LIMIT 1), 'Auditing', NULL, 12, 30000, 8, NULL),
-- PC 66
((SELECT PurchaseId FROM Purchases WHERE PcId=66 LIMIT 1), 'Course', 8, 0, 15000, 8, NULL),
-- PC 67
((SELECT PurchaseId FROM Purchases WHERE PcId=67 LIMIT 1), 'Auditing', NULL, 18, 45000, 8, NULL),
((SELECT PurchaseId FROM Purchases WHERE PcId=67 LIMIT 1), 'Course', 3, 0, 20000, 8, NULL),
-- PC 68
((SELECT PurchaseId FROM Purchases WHERE PcId=68 LIMIT 1), 'Auditing', NULL, 10, 25000, 8, NULL),
-- PC 69
((SELECT PurchaseId FROM Purchases WHERE PcId=69 LIMIT 1), 'Auditing', NULL, 20, 50000, 8, NULL),
-- PC 70
((SELECT PurchaseId FROM Purchases WHERE PcId=70 LIMIT 1), 'Course', 9, 0, 12000, 8, NULL),
-- PC 71
((SELECT PurchaseId FROM Purchases WHERE PcId=71 LIMIT 1), 'Auditing', NULL, 5, 12500, 8, NULL),
-- PC 72
((SELECT PurchaseId FROM Purchases WHERE PcId=72 LIMIT 1), 'Auditing', NULL, 15, 37500, 8, NULL),
-- PC 73
((SELECT PurchaseId FROM Purchases WHERE PcId=73 LIMIT 1), 'Auditing', NULL, 30, 75000, 8, NULL),
((SELECT PurchaseId FROM Purchases WHERE PcId=73 LIMIT 1), 'Course', 4, 0, 25000, 8, NULL),
((SELECT PurchaseId FROM Purchases WHERE PcId=73 LIMIT 1), 'Course', 10, 0, 10000, 8, NULL);

-- PurchasePaymentMethods for new purchases
INSERT INTO PurchasePaymentMethods (PurchaseId, MethodType, Amount, PaymentDate, IsMoneyInBank, MoneyInBankDate)
SELECT p.PurchaseId,
  CASE WHEN p.PurchaseId % 4 = 0 THEN 'Check'
       WHEN p.PurchaseId % 4 = 1 THEN 'Cash'
       WHEN p.PurchaseId % 4 = 2 THEN 'CreditCard'
       ELSE 'BankTransfer' END,
  COALESCE((SELECT SUM(AmountPaid) FROM PurchaseItems WHERE PurchaseId=p.PurchaseId), 0),
  p.PurchaseDate,
  1,
  p.PurchaseDate
FROM Purchases p
WHERE p.PurchaseId NOT IN (SELECT DISTINCT PurchaseId FROM PurchasePaymentMethods)
  AND p.PcId >= 54;

-- Legacy Payments for backward compat
INSERT INTO Payments (PcId, PaymentDate, HoursBought, AmountPaid, PaymentType, CourseId, PurchaseId)
SELECT pi.PurchaseId, p.PurchaseDate,
  CASE WHEN pi.ItemType='Auditing' THEN pi.HoursBought ELSE 0 END,
  pi.AmountPaid,
  pi.ItemType,
  pi.CourseId,
  p.PurchaseId
FROM PurchaseItems pi
JOIN Purchases p ON p.PurchaseId = pi.PurchaseId
WHERE p.PcId >= 54;

-- ============================================================
-- PART 10: StudentCourses - enrollments (some finished, some ongoing)
-- ============================================================
INSERT INTO StudentCourses (PersonId, CourseId, DateStarted, DateFinished) VALUES
-- Finished courses
(54, 1, '2026-02-05', '2026-03-01'),
(55, 1, '2026-02-05', '2026-02-28'),
(56, 2, '2026-02-06', '2026-03-05'),
(57, 5, '2026-02-06', '2026-03-08'),
(58, 3, '2026-02-07', '2026-03-03'),
(62, 7, '2026-02-10', '2026-03-06'),
(64, 1, '2026-02-11', '2026-03-09'),
(67, 3, '2026-02-14', '2026-03-10'),
-- Ongoing courses
(54, 5, '2026-03-01', NULL),
(55, 6, '2026-02-20', NULL),
(59, 6, '2026-02-08', NULL),
(60, 2, '2026-02-15', NULL),
(61, 1, '2026-02-20', NULL),
(63, 2, '2026-02-10', NULL),
(65, 4, '2026-02-25', NULL),
(66, 8, '2026-02-13', NULL),
(68, 1, '2026-02-15', NULL),
(69, 5, '2026-03-01', NULL),
(70, 9, '2026-03-02', NULL),
(71, 1, '2026-03-05', NULL),
(72, 3, '2026-03-07', NULL),
(73, 4, '2026-03-09', NULL),
(73, 10, '2026-03-09', NULL),
(74, 1, '2026-02-20', NULL),
(75, 2, '2026-02-25', NULL),
(76, 6, '2026-03-01', NULL),
(77, 1, '2026-02-19', NULL),
(78, 3, '2026-02-20', NULL),
(79, 5, '2026-02-19', NULL),
(80, 7, '2026-02-20', NULL),
(81, 8, '2026-02-21', NULL),
(82, 9, '2026-02-22', NULL),
(83, 10, '2026-02-23', NULL);

-- ============================================================
-- PART 11: AcademyAttendance - daily visits
-- ============================================================
INSERT OR IGNORE INTO AcademyAttendance (PersonId, VisitDate) VALUES
-- Spread visits across Feb-Mar for many PCs
(54,'2026-02-05'),(54,'2026-02-12'),(54,'2026-02-19'),(54,'2026-02-26'),(54,'2026-03-05'),
(55,'2026-02-05'),(55,'2026-02-06'),(55,'2026-02-12'),(55,'2026-02-19'),(55,'2026-03-03'),
(56,'2026-02-06'),(56,'2026-02-13'),(56,'2026-02-20'),(56,'2026-02-27'),(56,'2026-03-06'),
(57,'2026-02-06'),(57,'2026-02-07'),(57,'2026-02-14'),(57,'2026-02-21'),(57,'2026-03-07'),
(58,'2026-02-07'),(58,'2026-02-14'),(58,'2026-02-21'),(58,'2026-02-28'),(58,'2026-03-07'),
(59,'2026-02-08'),(59,'2026-02-15'),(59,'2026-02-22'),(59,'2026-03-01'),(59,'2026-03-08'),
(60,'2026-02-09'),(60,'2026-02-16'),(60,'2026-02-23'),(60,'2026-03-02'),(60,'2026-03-09'),
(61,'2026-02-09'),(61,'2026-02-10'),(61,'2026-02-16'),(61,'2026-02-23'),(61,'2026-03-09'),
(62,'2026-02-10'),(62,'2026-02-17'),(62,'2026-02-24'),(62,'2026-03-03'),(62,'2026-03-10'),
(63,'2026-02-10'),(63,'2026-02-11'),(63,'2026-02-18'),(63,'2026-02-25'),(63,'2026-03-10'),
(64,'2026-02-11'),(64,'2026-02-18'),(64,'2026-02-25'),(64,'2026-03-04'),
(65,'2026-02-12'),(65,'2026-02-19'),(65,'2026-02-26'),(65,'2026-03-05'),
(66,'2026-02-13'),(66,'2026-02-20'),(66,'2026-02-27'),(66,'2026-03-06'),
(67,'2026-02-12'),(67,'2026-02-14'),(67,'2026-02-21'),(67,'2026-03-07'),
(68,'2026-02-13'),(68,'2026-02-15'),(68,'2026-02-22'),(68,'2026-03-08'),
(69,'2026-02-14'),(69,'2026-02-21'),(69,'2026-02-28'),(69,'2026-03-07'),
(70,'2026-02-15'),(70,'2026-02-22'),(70,'2026-03-01'),
(71,'2026-02-15'),(71,'2026-02-23'),(71,'2026-03-02'),
(72,'2026-02-16'),(72,'2026-02-24'),(72,'2026-03-03'),
(73,'2026-02-17'),(73,'2026-02-25'),(73,'2026-03-04'),
(74,'2026-02-18'),(74,'2026-02-26'),(74,'2026-03-05'),
(75,'2026-02-16'),(75,'2026-02-23'),(75,'2026-03-02'),
(76,'2026-02-17'),(76,'2026-02-24'),(76,'2026-03-03'),
(77,'2026-02-19'),(77,'2026-02-26'),(77,'2026-03-05'),
(78,'2026-02-20'),(78,'2026-02-27'),(78,'2026-03-06'),
(79,'2026-02-19'),(79,'2026-02-26'),(79,'2026-03-05'),
(80,'2026-02-20'),(80,'2026-02-27'),(80,'2026-03-06'),
(81,'2026-02-21'),(81,'2026-02-28'),(81,'2026-03-07'),
(82,'2026-02-22'),(82,'2026-03-01'),(82,'2026-03-08'),
(83,'2026-02-23'),(83,'2026-03-02'),(83,'2026-03-09'),
(84,'2026-02-24'),(84,'2026-03-03'),
(85,'2026-02-25'),(85,'2026-03-04'),
(86,'2026-02-24'),(86,'2026-03-03'),
(87,'2026-02-25'),(87,'2026-03-04'),
(88,'2026-02-26'),(88,'2026-03-05'),
(89,'2026-02-27'),(89,'2026-03-06'),
(90,'2026-02-26'),(90,'2026-03-05'),
(91,'2026-02-27'),(91,'2026-03-06'),
(92,'2026-02-28'),(92,'2026-03-07'),
(93,'2026-03-01'),(93,'2026-03-08'),
(94,'2026-03-02'),(94,'2026-03-09'),
(95,'2026-03-03'),(95,'2026-03-10'),
(96,'2026-03-04'),
(97,'2026-03-03'),
(98,'2026-03-04'),
(99,'2026-03-05'),
(100,'2026-03-06'),
(101,'2026-03-05'),
(102,'2026-03-06'),
(103,'2026-03-07');

-- ============================================================
-- PART 12: StaffPcList for new PCs (assign to auditors)
-- ============================================================
INSERT OR IGNORE INTO StaffPcList (UserId, PcId, WorkCapacity) VALUES
(1, 54, 'Auditor'), (1, 55, 'Auditor'), (1, 64, 'Auditor'), (1, 65, 'Auditor'),
(1, 66, 'CS'), (1, 77, 'Auditor'), (1, 78, 'Auditor'), (1, 88, 'Auditor'),
(1, 89, 'Auditor'), (1, 99, 'Auditor'), (1, 100, 'Auditor'),
(6, 56, 'Auditor'), (6, 57, 'Auditor'), (6, 67, 'Auditor'), (6, 68, 'Auditor'),
(6, 69, 'CS'), (6, 79, 'Auditor'), (6, 80, 'Auditor'), (6, 81, 'Auditor'),
(6, 90, 'Auditor'), (6, 91, 'Auditor'), (6, 92, 'Auditor'),
(6, 101, 'Auditor'), (6, 102, 'Auditor'), (6, 103, 'Auditor'),
(3, 58, 'Auditor'), (3, 59, 'Auditor'), (3, 70, 'Auditor'), (3, 82, 'Auditor'),
(3, 93, 'Auditor'), (3, 54, 'CS'),
(5, 60, 'Auditor'), (5, 61, 'Auditor'), (5, 72, 'Auditor'), (5, 83, 'Auditor'),
(5, 94, 'Auditor'), (5, 55, 'CS'),
(7, 62, 'Auditor'), (7, 63, 'Auditor'), (7, 73, 'Auditor'), (7, 84, 'Auditor'),
(7, 85, 'Auditor'), (7, 95, 'Auditor'), (7, 96, 'Auditor'),
(8, 75, 'Auditor'), (8, 76, 'Auditor'), (8, 86, 'Auditor'), (8, 87, 'Auditor'),
(8, 97, 'Auditor'), (8, 98, 'Auditor'), (8, 64, 'CS');

-- ============================================================
-- PART 13: AuditorPcPermissions for new PCs
-- ============================================================
INSERT OR IGNORE INTO AuditorPcPermissions (AuditorId, PcId, IsApproved, RequestedAt) VALUES
(1, 54, 1, '2026-02-05'), (1, 55, 1, '2026-02-05'), (1, 64, 1, '2026-02-11'),
(1, 65, 1, '2026-02-12'), (1, 66, 1, '2026-02-13'), (1, 77, 1, '2026-02-19'),
(1, 78, 1, '2026-02-20'), (1, 88, 1, '2026-02-26'), (1, 89, 1, '2026-02-27'),
(1, 99, 1, '2026-03-05'), (1, 100, 1, '2026-03-06'),
(6, 56, 1, '2026-02-06'), (6, 57, 1, '2026-02-06'), (6, 67, 1, '2026-02-12'),
(6, 68, 1, '2026-02-13'), (6, 69, 1, '2026-02-14'), (6, 79, 1, '2026-02-19'),
(6, 80, 1, '2026-02-20'), (6, 81, 1, '2026-02-21'), (6, 90, 1, '2026-02-26'),
(6, 91, 1, '2026-02-27'), (6, 92, 1, '2026-02-28'), (6, 101, 1, '2026-03-05'),
(6, 102, 1, '2026-03-06'), (6, 103, 1, '2026-03-07'),
(3, 58, 1, '2026-02-07'), (3, 59, 1, '2026-02-08'), (3, 70, 1, '2026-02-15'),
(3, 82, 1, '2026-02-22'), (3, 93, 1, '2026-03-01'), (3, 54, 1, '2026-03-08'),
(5, 60, 1, '2026-02-09'), (5, 61, 1, '2026-02-09'), (5, 72, 1, '2026-02-16'),
(5, 83, 1, '2026-02-23'), (5, 94, 1, '2026-03-02'), (5, 55, 1, '2026-03-09'),
(7, 62, 1, '2026-02-10'), (7, 63, 1, '2026-02-10'), (7, 73, 1, '2026-02-17'),
(7, 84, 1, '2026-02-24'), (7, 85, 1, '2026-02-25'), (7, 95, 1, '2026-03-03'),
(7, 96, 1, '2026-03-04'), (7, 56, 1, '2026-03-10'),
(8, 75, 1, '2026-02-16'), (8, 76, 1, '2026-02-17'), (8, 86, 1, '2026-02-24'),
(8, 87, 1, '2026-02-25'), (8, 97, 1, '2026-03-03'), (8, 98, 1, '2026-03-04'),
(8, 64, 1, '2026-03-10'), (8, 65, 1, '2026-03-10');

-- ============================================================
-- PART 14: MiscCharge entries
-- ============================================================
INSERT INTO MiscCharge (AuditorId, PcId, ChargeDate, SequenceInDay, LengthSeconds, AdminSeconds, IsFree, Summary) VALUES
(1, 54, '2026-02-10', 1, 1800, 300, 0, 'Admin paperwork'),
(6, 57, '2026-02-15', 1, 2400, 300, 0, 'File organization'),
(3, 58, '2026-02-20', 1, 1200, 0, 1, 'Free consultation'),
(1, 65, '2026-02-25', 1, 3600, 600, 0, 'Extended admin'),
(6, 80, '2026-03-01', 1, 1800, 300, 0, 'Case prep'),
(7, 85, '2026-03-05', 1, 2400, 300, 0, 'Documentation'),
(5, 60, '2026-03-08', 1, 1200, 0, 1, 'Quick followup'),
(8, 97, '2026-03-09', 1, 3600, 600, 0, 'Admin review');

-- ============================================================
-- PART 15: WeeklyRemarks
-- ============================================================
INSERT OR IGNORE INTO WeeklyRemarks (AuditorId, WeekDate, Remarks) VALUES
(1, '2026-02-05', 'Good progress with new PCs this week'),
(6, '2026-02-05', 'Started several new cases'),
(1, '2026-02-12', 'Follow-up sessions going well'),
(6, '2026-02-12', 'Need more time for case reviews'),
(3, '2026-02-19', 'Challenging week but productive'),
(5, '2026-02-19', 'All sessions completed on time'),
(7, '2026-02-26', 'New PCs settling in nicely'),
(8, '2026-02-26', 'Admin backlog cleared'),
(1, '2026-03-05', 'March starting strong'),
(6, '2026-03-05', 'Multiple sessions this week');

-- ============================================================
-- PART 16: StaffMessages
-- ============================================================
INSERT INTO StaffMessages (FromStaffId, ToStaffId, MsgText, CreatedAt, AcknowledgedAt) VALUES
(8, 1, 'Please review PC 54 progress', '2026-02-10 09:00:00', '2026-02-10 10:30:00'),
(1, 8, 'Reviewed - looks good', '2026-02-10 10:35:00', '2026-02-10 11:00:00'),
(8, 6, 'New PC 57 assigned to you', '2026-02-12 08:00:00', '2026-02-12 08:45:00'),
(6, 8, 'Got it, scheduled for Thursday', '2026-02-12 09:00:00', NULL),
(8, 3, 'PC 58 needs attention', '2026-02-15 14:00:00', '2026-02-15 15:00:00'),
(4, 8, 'CS review completed for this week', '2026-02-20 16:00:00', '2026-02-20 17:00:00'),
(8, 7, 'Great work on PC 62 and 63', '2026-02-25 10:00:00', NULL),
(5, 8, 'Session with PC 60 went overtime', '2026-03-02 15:00:00', '2026-03-02 16:00:00'),
(8, 1, 'March schedule looks packed', '2026-03-05 08:00:00', NULL),
(6, 1, 'Can you cover PC 80 on Thursday?', '2026-03-08 09:00:00', NULL);

-- ============================================================
-- PART 17: Remove orphan/unreferenced data
-- ============================================================
-- Remove PCs that have no sessions, no payments, no purchases, no staffpclist entries, and are not staff
DELETE FROM PCs WHERE PcId NOT IN (
  SELECT DISTINCT PcId FROM Sessions
  UNION SELECT DISTINCT PcId FROM Payments
  UNION SELECT DISTINCT PcId FROM Purchases
  UNION SELECT DISTINCT PcId FROM StaffPcList
  UNION SELECT DISTINCT PcId FROM AuditorPcPermissions
  UNION SELECT AuditorId FROM Auditors
  UNION SELECT CsId FROM CaseSupervisors
) AND PcId NOT IN (SELECT PersonId FROM Persons WHERE PersonId <= 8);
