--
-- PostgreSQL database dump
--

\restrict kWqp3UXUQ79EVevV0N7NkWL7KFWq6MSqZuRip3SAulWNXpx3bFNAy4ThgXuVezS

-- Dumped from database version 16.13
-- Dumped by pg_dump version 16.13

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: BlackjackHands; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."BlackjackHands" (
    "UserId" bigint NOT NULL,
    "Bet" integer NOT NULL,
    "PlayerCards" text NOT NULL,
    "DealerCards" text NOT NULL,
    "DeckState" text NOT NULL,
    "ChatId" bigint NOT NULL,
    "StateMessageId" integer,
    "CreatedAt" bigint NOT NULL
);


ALTER TABLE public."BlackjackHands" OWNER TO cazino;

--
-- Name: Chats; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."Chats" (
    "ChatId" bigint NOT NULL,
    "Name" text NOT NULL,
    "Username" text,
    "NotificationsEnabled" boolean NOT NULL
);


ALTER TABLE public."Chats" OWNER TO cazino;

--
-- Name: DartsBets; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."DartsBets" (
    "UserId" bigint NOT NULL,
    "ChatId" bigint NOT NULL,
    "Amount" integer NOT NULL,
    "CreatedAt" bigint NOT NULL
);


ALTER TABLE public."DartsBets" OWNER TO cazino;

--
-- Name: DiceCubeBets; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."DiceCubeBets" (
    "UserId" bigint NOT NULL,
    "ChatId" bigint NOT NULL,
    "Amount" integer NOT NULL,
    "CreatedAt" bigint NOT NULL
);


ALTER TABLE public."DiceCubeBets" OWNER TO cazino;

--
-- Name: DisplayNameOverrides; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."DisplayNameOverrides" (
    "OriginalName" text NOT NULL,
    "NewName" text NOT NULL
);


ALTER TABLE public."DisplayNameOverrides" OWNER TO cazino;

--
-- Name: FreespinCodes; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."FreespinCodes" (
    "Code" uuid NOT NULL,
    "Active" boolean NOT NULL,
    "IssuedBy" bigint NOT NULL,
    "IssuedAt" bigint NOT NULL,
    "ChatId" bigint,
    "MessageId" integer
);


ALTER TABLE public."FreespinCodes" OWNER TO cazino;

--
-- Name: HorseBets; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."HorseBets" (
    "Id" uuid NOT NULL,
    "RaceDate" text NOT NULL,
    "HorseId" integer NOT NULL,
    "Amount" integer NOT NULL,
    "UserId" bigint NOT NULL
);


ALTER TABLE public."HorseBets" OWNER TO cazino;

--
-- Name: HorseResults; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."HorseResults" (
    "RaceDate" text NOT NULL,
    "Winner" integer NOT NULL,
    "ImageData" bytea NOT NULL
);


ALTER TABLE public."HorseResults" OWNER TO cazino;

--
-- Name: PokerSeats; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."PokerSeats" (
    "InviteCode" text NOT NULL,
    "Position" integer NOT NULL,
    "UserId" bigint NOT NULL,
    "DisplayName" text NOT NULL,
    "Stack" integer NOT NULL,
    "HoleCards" text NOT NULL,
    "Status" integer NOT NULL,
    "CurrentBet" integer NOT NULL,
    "HasActedThisRound" boolean NOT NULL,
    "ChatId" bigint NOT NULL,
    "StateMessageId" integer,
    "JoinedAt" bigint NOT NULL
);


ALTER TABLE public."PokerSeats" OWNER TO cazino;

--
-- Name: PokerTables; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."PokerTables" (
    "InviteCode" text NOT NULL,
    "HostUserId" bigint NOT NULL,
    "Status" integer NOT NULL,
    "Phase" integer NOT NULL,
    "SmallBlind" integer NOT NULL,
    "BigBlind" integer NOT NULL,
    "Pot" integer NOT NULL,
    "CommunityCards" text NOT NULL,
    "DeckState" text NOT NULL,
    "ButtonSeat" integer NOT NULL,
    "CurrentSeat" integer NOT NULL,
    "CurrentBet" integer NOT NULL,
    "MinRaise" integer NOT NULL,
    "LastActionAt" bigint NOT NULL,
    "CreatedAt" bigint NOT NULL
);


ALTER TABLE public."PokerTables" OWNER TO cazino;

--
-- Name: SecretHitlerGames; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."SecretHitlerGames" (
    "InviteCode" text NOT NULL,
    "HostUserId" bigint NOT NULL,
    "ChatId" bigint NOT NULL,
    "Status" integer NOT NULL,
    "Phase" integer NOT NULL,
    "LiberalPolicies" integer NOT NULL,
    "FascistPolicies" integer NOT NULL,
    "ElectionTracker" integer NOT NULL,
    "CurrentPresidentPosition" integer NOT NULL,
    "NominatedChancellorPosition" integer NOT NULL,
    "LastElectedPresidentPosition" integer NOT NULL,
    "LastElectedChancellorPosition" integer NOT NULL,
    "DeckState" text NOT NULL,
    "DiscardState" text NOT NULL,
    "PresidentDraw" text NOT NULL,
    "ChancellorReceived" text NOT NULL,
    "Winner" integer NOT NULL,
    "WinReason" integer NOT NULL,
    "BuyIn" integer NOT NULL,
    "Pot" integer NOT NULL,
    "CreatedAt" bigint NOT NULL,
    "LastActionAt" bigint NOT NULL
);


ALTER TABLE public."SecretHitlerGames" OWNER TO cazino;

--
-- Name: SecretHitlerPlayers; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."SecretHitlerPlayers" (
    "InviteCode" text NOT NULL,
    "Position" integer NOT NULL,
    "UserId" bigint NOT NULL,
    "DisplayName" text NOT NULL,
    "ChatId" bigint NOT NULL,
    "Role" integer NOT NULL,
    "IsAlive" boolean NOT NULL,
    "LastVote" integer NOT NULL,
    "StateMessageId" integer,
    "JoinedAt" bigint NOT NULL
);


ALTER TABLE public."SecretHitlerPlayers" OWNER TO cazino;

--
-- Name: Users; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."Users" (
    "TelegramUserId" bigint NOT NULL,
    "DisplayName" text NOT NULL,
    "Coins" integer NOT NULL,
    "LastDayUtc" bigint NOT NULL,
    "AttemptCount" integer NOT NULL,
    "ExtraAttempts" integer NOT NULL,
    "BlackjackHandsPlayed" integer NOT NULL,
    "Version" bigint DEFAULT 0 NOT NULL
);


ALTER TABLE public."Users" OWNER TO cazino;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: cazino
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


ALTER TABLE public."__EFMigrationsHistory" OWNER TO cazino;

--
-- Data for Name: BlackjackHands; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."BlackjackHands" ("UserId", "Bet", "PlayerCards", "DealerCards", "DeckState", "ChatId", "StateMessageId", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: Chats; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."Chats" ("ChatId", "Name", "Username", "NotificationsEnabled") FROM stdin;
\.


--
-- Data for Name: DartsBets; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."DartsBets" ("UserId", "ChatId", "Amount", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: DiceCubeBets; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."DiceCubeBets" ("UserId", "ChatId", "Amount", "CreatedAt") FROM stdin;
\.


--
-- Data for Name: DisplayNameOverrides; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."DisplayNameOverrides" ("OriginalName", "NewName") FROM stdin;
\.


--
-- Data for Name: FreespinCodes; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."FreespinCodes" ("Code", "Active", "IssuedBy", "IssuedAt", "ChatId", "MessageId") FROM stdin;
\.


--
-- Data for Name: HorseBets; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."HorseBets" ("Id", "RaceDate", "HorseId", "Amount", "UserId") FROM stdin;
\.


--
-- Data for Name: HorseResults; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."HorseResults" ("RaceDate", "Winner", "ImageData") FROM stdin;
04-18-2026	3	\\x89504e470d0a1a0a0000000d49484452000001f4000000d20806000000bc5432150000000473424954080808087c086488000009da49444154789ceddd4d885d6719c0f1e7dc3b33cdc44c94a61f74868acda41b4daa12506aada8d00f3f40045d84bad145bb10bba8d285821ba10531ddb873a12e2c1114dd69d28a94d4562c04b549054d9a743353634c95ce34632673efeb6286984a9de4de7b92737dceefb7c9e69ee73ebb3fefcd9973aa524a0900e0ff5aa7e9050080d1093a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e80090c044d30b008ca3b2b010fd63c7a2bfb010e5b5d7a22c2e46677e3eaab9b9e8cccd45353f1fd5f5d737bd265c5495524ad34b008c8d9595583b74287a870f6ffeb92d5b6272dfbee8ecd973ad36834d093ac086fe8913b176e040947ffce38aafe9ecde1d93fbf6454c4f5fd5dde072041d60e327f6d5fdfb87bab6333f1f935ffe72ed3bc120dc14071011170e1c18fadafecb2fc7dac183b5ee03831274a0f5d60e1e8cb2b838d28cde534f455958a86d271894a003add77beaa95ae6ac1d3a54cb1c1886a003ad56e7a9da099d26093ad06afd3a833ec0ddf150b7c61e2c73fe91479afa6a80aba67fe2447476ed6a7a0d5ac8091d0012f077e840abf55e7821d67efce3dae65df7c413b5cd824138a103add6999bab6d56353b5bdb2c1894a003ad56d519742f6ba141820eb45ef7de7b6b993371df7db5cc8161083ad07a13f7df3ff2cfe5dd7befadf5b40f8312748088f537a60da99a9f8f89fbefaf751f1894bbdc0136f44f9c587f498bd7a7f27f48d0012eb5b2126b870e45eff0e1cd3fb7654b4ceedb179d3d7baed566b0294107780b6561217a478f46595c8cf2da6b511617a39a9f8fcedc5c54b3b3d1ddb3c7a99cb122e80090809be200200141078004041d00121074004840d001200141078004041d00121074004840d001200141078004041d00121074004840d001200141078004041d00121074004840d001200141078004041d00121074004840d001200141078004041d00121074004840d001200141078004041d00121074004840d001208189a61700184b678e47ffd46fa29c391ee5ccf188f3cb51dd787b5437ee8aea86dba3daf9e188eb669ade122eaa4a29a5e92500c6c6f9a5e8fdeaf128279fddfc7353dba2fbe9c7a29a7bffb5da0c3625e8001bcac9c3d17bfaf188d5e52bbea6f3becf47e7035f745aa771820e101165e1f7d1fbd9c3435d5bedbc3bba9f7aacf69d60106e8a0338bf14bda7870f7239f96c9493876b5d090625e840ebf57ff7fd88a5bf8e34a3f7f4e311e7976adb090625e840bbbdfe6af4fff8d3d1e7ac2e47ff0f3fa96323188aa003ad56fe7ebcbe590bbfaf6d160c4ad081562b674e8ce52c1854630f9659fbeedd4d7d35c07f94125155f5cc5a5d8e78fdd588edb7d4330f06e0840e0009f83b74a0d5fabffb7ef45ff8413dc3a6de16130f1dac67160cc8091d68b5eac65d35cebabdb659302841075aadbaa1be087bae3b4d1274a0ddb6df129df77e6ef439536f8bcefb3e5fc746301441075aaff3c12f45ccdc3cd28cee3d5ff782161a25e800d7cd44f79e6f0c7d7975db87a3daf9915a578241b9cb1d60c3faeb531f8b587de38aafe9bcf773eb277ca7731a26e80097da78f35a39f59bcd3f37737374eff9861be1181b820ef056ce1c8ffec967a39c39befebcf7f3cb51dd787b5437ec5aff77e7dd4ee58c1541078004dc14070009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e00094c34bd00c0b85a7dfe95e89d3a1b175e5c8cdee9a528cbab3131bf2326ef988deece1d3175e7bb9a5e112eaa4a29a5e92500c649fff4522c3ff14c5c78f1d54d3fd7ddb923661ef96874e7775cb3dde07f1174804bfcebe747e3dc9347a2bcb17ac5d76cfdc2de987e60ef55dd0b2e47d00136fcebe747e38deffd76a86ba71fd81b5bbf20ea34c74d71001b3fb39f7bf2c8d0d7af3c79247a2f9fad75271884a00344c4d2fe6706fa99fdadbcfead435196479b01c31274a0f52ebcb8186b4737bf01ee4af44f2fc7f9a7ff5ccb4e302841075aef7277b30fa2777aa9b659300841075aaf5f6384d74efa7f749ad1d88365ce7ee27b4d7d35c09b95125155b58caae3a77b1886133a0024d0d8097dc72f1f6ceaab01de6479ff3371fe577fa965d6c49e5b6a9903837242075aaf73f34c6db326767a0c2ccd1074a0f526efa8ef543de1b9ee3444d081d69bbc63b6969fca3b376d8ba93b6fab65271894a00344c4cc573f1ad5d6a991666cffe67d516d1b6d060c4bd00136fe1f7d9497ab4c3fb0d76b5469546377b9038c9b2d9fdd131111e77e7424cab92b7f26bb37ad310ebc3e15e0bff44f2fc5d2fe672efb9098ee6dd7c7cc573fe664ce58107480ff61f5f95762ede4d9b8f0e262f44f2f45595e8deefc8ef59be876ee88a90fbdabe915e12241078004dc14070009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e00094c34bd00c0382afffc45949597a2bff45c9473c722ba6f8feaba5ba333735754d3bba37ac7279a5e11dea42aa594a69700181765e558f44e3d1c65e5d8a69faba677c7c4ae1f464cbdf39aed069b1174800dbdc56f47ffd5ef5cf905ddedd19d7d343a373d7835d7822b22e800c3c4fc12dd5bbf159d9b1eaa7d271884a003ad57568ec5da9f3e3efc80eef6987cf7affdfc4ea3dce50eb45eefd457461cf07aacbdf2705debc050041d68b5fed90351565e1a794e597a3ecad273b5ec04c31074a0d5cab9cdef661f445fd06990a003ad5667d02ff7a76e703535f660990b476e6aeaab012e5122a2aa67d2b9d17fba876139a103d4c61f0dd19cc64ee8937bffd6d457035cb4f6e7cf4459fe6d2db3aaadbb6b9903c37042075aadce0857d3824e73041d68b53a83de99b9abb659302841075aadb3635f54d3ef19794eb5edcea8049d06093ad07adddbbe3be28099981875068c48d081d6aba67747e796af0d7d7d77f651cf71a7718ddde50e304ebab38f46440cf8fad4998dd7a77ad31acdf3b635804b949563d13bf595cb3edfbdda76e7facfec4ee68c094107780be59fbf88feb96351969e5b7fa46bf7ed514ddd1ad5cc5dd1d9ba3baa777cb2e915e14d041d001270531c002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002820e0009083a002420e8009080a0034002ff06288b5a35f5aaa1d30000000049454e44ae426082
\.


--
-- Data for Name: PokerSeats; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."PokerSeats" ("InviteCode", "Position", "UserId", "DisplayName", "Stack", "HoleCards", "Status", "CurrentBet", "HasActedThisRound", "ChatId", "StateMessageId", "JoinedAt") FROM stdin;
HSWFK	0	925337014	cppshizoid	500		0	0	f	925337014	\N	1776502583951
\.


--
-- Data for Name: PokerTables; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."PokerTables" ("InviteCode", "HostUserId", "Status", "Phase", "SmallBlind", "BigBlind", "Pot", "CommunityCards", "DeckState", "ButtonSeat", "CurrentSeat", "CurrentBet", "MinRaise", "LastActionAt", "CreatedAt") FROM stdin;
HSWFK	925337014	0	0	5	10	0			0	0	0	0	1776502583951	1776502583951
\.


--
-- Data for Name: SecretHitlerGames; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."SecretHitlerGames" ("InviteCode", "HostUserId", "ChatId", "Status", "Phase", "LiberalPolicies", "FascistPolicies", "ElectionTracker", "CurrentPresidentPosition", "NominatedChancellorPosition", "LastElectedPresidentPosition", "LastElectedChancellorPosition", "DeckState", "DiscardState", "PresidentDraw", "ChancellorReceived", "Winner", "WinReason", "BuyIn", "Pot", "CreatedAt", "LastActionAt") FROM stdin;
N7M48	925337014	925337014	0	0	0	0	0	0	-1	-1	-1					0	0	50	50	1776527965954	1776527965954
\.


--
-- Data for Name: SecretHitlerPlayers; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."SecretHitlerPlayers" ("InviteCode", "Position", "UserId", "DisplayName", "ChatId", "Role", "IsAlive", "LastVote", "StateMessageId", "JoinedAt") FROM stdin;
N7M48	0	925337014	cppshizoid	925337014	0	t	0	\N	1776527965954
\.


--
-- Data for Name: Users; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."Users" ("TelegramUserId", "DisplayName", "Coins", "LastDayUtc", "AttemptCount", "ExtraAttempts", "BlackjackHandsPlayed", "Version") FROM stdin;
1176353195	D1orn	100	1776445200000	0	0	0	2
788961289	Random_user28	191	1776445200000	0	0	0	8
925337014	cppshizoid	997	1776445200000	3	0	1	23
\.


--
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: public; Owner: cazino
--

COPY public."__EFMigrationsHistory" ("MigrationId", "ProductVersion") FROM stdin;
20260420000000_InitialCreate	10.0.0
20260420000001_AddDiceCubeBets	10.0.0
20260421000000_AddDartsBets	10.0.0
20260422000000_AddSecretHitler	10.0.0
\.


--
-- Name: BlackjackHands PK_BlackjackHands; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."BlackjackHands"
    ADD CONSTRAINT "PK_BlackjackHands" PRIMARY KEY ("UserId");


--
-- Name: Chats PK_Chats; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."Chats"
    ADD CONSTRAINT "PK_Chats" PRIMARY KEY ("ChatId");


--
-- Name: DartsBets PK_DartsBets; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."DartsBets"
    ADD CONSTRAINT "PK_DartsBets" PRIMARY KEY ("UserId", "ChatId");


--
-- Name: DiceCubeBets PK_DiceCubeBets; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."DiceCubeBets"
    ADD CONSTRAINT "PK_DiceCubeBets" PRIMARY KEY ("UserId", "ChatId");


--
-- Name: DisplayNameOverrides PK_DisplayNameOverrides; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."DisplayNameOverrides"
    ADD CONSTRAINT "PK_DisplayNameOverrides" PRIMARY KEY ("OriginalName");


--
-- Name: FreespinCodes PK_FreespinCodes; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."FreespinCodes"
    ADD CONSTRAINT "PK_FreespinCodes" PRIMARY KEY ("Code");


--
-- Name: HorseBets PK_HorseBets; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."HorseBets"
    ADD CONSTRAINT "PK_HorseBets" PRIMARY KEY ("Id");


--
-- Name: HorseResults PK_HorseResults; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."HorseResults"
    ADD CONSTRAINT "PK_HorseResults" PRIMARY KEY ("RaceDate");


--
-- Name: PokerSeats PK_PokerSeats; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."PokerSeats"
    ADD CONSTRAINT "PK_PokerSeats" PRIMARY KEY ("InviteCode", "Position");


--
-- Name: PokerTables PK_PokerTables; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."PokerTables"
    ADD CONSTRAINT "PK_PokerTables" PRIMARY KEY ("InviteCode");


--
-- Name: SecretHitlerGames PK_SecretHitlerGames; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."SecretHitlerGames"
    ADD CONSTRAINT "PK_SecretHitlerGames" PRIMARY KEY ("InviteCode");


--
-- Name: SecretHitlerPlayers PK_SecretHitlerPlayers; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."SecretHitlerPlayers"
    ADD CONSTRAINT "PK_SecretHitlerPlayers" PRIMARY KEY ("InviteCode", "Position");


--
-- Name: Users PK_Users; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("TelegramUserId");


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: cazino
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: IX_FreespinCodes_Active; Type: INDEX; Schema: public; Owner: cazino
--

CREATE INDEX "IX_FreespinCodes_Active" ON public."FreespinCodes" USING btree ("Active");


--
-- Name: IX_HorseBets_RaceDate_UserId; Type: INDEX; Schema: public; Owner: cazino
--

CREATE INDEX "IX_HorseBets_RaceDate_UserId" ON public."HorseBets" USING btree ("RaceDate", "UserId");


--
-- Name: IX_PokerSeats_UserId; Type: INDEX; Schema: public; Owner: cazino
--

CREATE INDEX "IX_PokerSeats_UserId" ON public."PokerSeats" USING btree ("UserId");


--
-- Name: IX_PokerTables_Status; Type: INDEX; Schema: public; Owner: cazino
--

CREATE INDEX "IX_PokerTables_Status" ON public."PokerTables" USING btree ("Status");


--
-- Name: IX_SecretHitlerGames_Status; Type: INDEX; Schema: public; Owner: cazino
--

CREATE INDEX "IX_SecretHitlerGames_Status" ON public."SecretHitlerGames" USING btree ("Status");


--
-- Name: IX_SecretHitlerPlayers_UserId; Type: INDEX; Schema: public; Owner: cazino
--

CREATE INDEX "IX_SecretHitlerPlayers_UserId" ON public."SecretHitlerPlayers" USING btree ("UserId");


--
-- PostgreSQL database dump complete
--

\unrestrict kWqp3UXUQ79EVevV0N7NkWL7KFWq6MSqZuRip3SAulWNXpx3bFNAy4ThgXuVezS

