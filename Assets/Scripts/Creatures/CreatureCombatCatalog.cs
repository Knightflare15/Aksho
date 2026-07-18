using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CreatureCombatCatalog", menuName = "The Script/Content/Creature Combat Catalog")]
public partial class CreatureCombatCatalog : ScriptableObject
{
    public static readonly string[] PronunciationBackedConcreteNouns =
    {
        "ANT", "APPLE", "ARM", "BAG", "BALL", "BAT", "BED", "BELL", "BIN", "BIRD",
        "BOAT", "BOOK", "BOX", "BROOM", "BUN", "BUS", "CAN", "CAP", "CAR", "CAT",
        "COW", "CUP", "DAD", "DOG", "DOLL", "DOOR", "DOT", "DRUM", "DUCK", "EAR",
        "EGG", "EYE", "FAN", "FIN", "FISH", "FOX", "GAP", "GOAT", "GRAPES", "GUM",
        "HAT", "HEN", "ICE", "INK", "JAM", "JAR", "JUG", "KEY", "KID", "KING",
        "KITE", "LEG", "LION", "LOG", "MAN", "MAT", "MEN", "MOON", "MOP", "MUG",
        "NEST", "NET", "NOSE", "NUT", "OWL", "OX", "PAN", "PEG", "PEN", "PET",
        "PARK", "PIG", "PIN", "POT", "PUP", "QUAIL", "QUILT", "RAIN", "RAT", "RING", "ROCK", "RUG",
        "SCHOOL", "SHOP", "SOCK", "SPOON", "STAR", "SUN", "TAP", "TIGER", "TOY", "TREE", "UMBRELLA",
        "UNICORN", "VAN", "VASE", "WALL", "WATCH", "WIG", "XRAY", "XYLOPHONE",
        "YAK", "YOYO", "ZEBRA"
    };

    // Authored enemies can use concrete creature families that are not part of
    // the early pronunciation-backed summon curriculum. Keep this list
    // separate so validating encounter content does not silently widen the
    // words offered by pronunciation exercises.
    public static readonly string[] AuthoredEnemyCreatureFamilyNouns =
    {
        "RABBIT", "CRAB"
    };

    // Scene landmarks belong in the grammar vocabulary, but not in the creature
    // family pool used to generate summons and encounters.
    public static readonly string[] ContextualTerrainNouns =
    {
        "ROOF", "BRIDGE"
    };

    public List<NounDefinition> nouns = new List<NounDefinition>();
    public List<VerbActionDefinition> verbs = new List<VerbActionDefinition>();
    public List<ModifierDefinition> modifiers = new List<ModifierDefinition>();

    public static CreatureCombatCatalog CreateRuntimeDefault()
    {
        CreatureCombatCatalog catalog = CreateInstance<CreatureCombatCatalog>();
        catalog.name = "Runtime Creature Combat Defaults";
        catalog.verbs = BuildDefaultVerbs();
        catalog.modifiers = BuildDefaultModifiers();
        catalog.nouns = BuildDefaultNouns(catalog.verbs, catalog.modifiers);
        return catalog;
    }

    static List<VerbActionDefinition> BuildDefaultVerbs()
    {
        return new List<VerbActionDefinition>
        {
            BuildVerb("ATTACK", BattleActionRole.Offense, 2, 2, 0.9f, 3f, "Attack", "OFFENSE", "UTILITY", aliases: new[] { "HIT", "STRIKE" }, third: new[] { "ATTACKS", "HITS", "STRIKES" }, past: new[] { "ATTACKED", "HIT", "STRUCK" }, progressive: new[] { "ATTACKING", "HITTING", "STRIKING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("SCRATCH", BattleActionRole.Offense, 2, 2, 0.92f, 3.5f, "Attack", "OFFENSE", "ANIMAL", aliases: new[] { "CLAW", "PAW" }, third: new[] { "SCRATCHES", "CLAWS", "PAWS" }, past: new[] { "SCRATCHED", "CLAWED", "PAWED" }, progressive: new[] { "SCRATCHING", "CLAWING", "PAWING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("BITE", BattleActionRole.Offense, 3, 3, 0.84f, 2.8f, "Attack", "OFFENSE", "ANIMAL", aliases: new[] { "NIP" }, third: new[] { "BITES", "NIPS" }, past: new[] { "BIT", "NIPPED" }, progressive: new[] { "BITING", "NIPPING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("PECK", BattleActionRole.Offense, 2, 2, 0.94f, 2.6f, "Attack", "OFFENSE", "BIRD", third: new[] { "PECKS" }, past: new[] { "PECKED" }, progressive: new[] { "PECKING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("WALK", BattleActionRole.Dodge, 1, 0, 1f, 3f, "Move", "MOVEMENT", "UTILITY", movementVerb: true, movementProfile: "walk", aliases: new[] { "MOVE", "STEP" }, third: new[] { "WALKS", "MOVES", "STEPS" }, past: new[] { "WALKED", "MOVED", "STEPPED" }, progressive: new[] { "WALKING", "MOVING", "STEPPING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("RUN", BattleActionRole.Dodge, 2, 0, 1f, 6f, "Move", "MOVEMENT", "UTILITY", movementVerb: true, movementProfile: "run", aliases: new[] { "DASH", "SPRINT" }, third: new[] { "RUNS", "DASHES", "SPRINTS" }, past: new[] { "RAN", "DASHED", "SPRINTED" }, progressive: new[] { "RUNNING", "DASHING", "SPRINTING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("DODGE", BattleActionRole.Dodge, 2, 0, 1f, 5.5f, "Move", "MOVEMENT", "DEFENSE", movementVerb: true, movementProfile: "dodge", aliases: new[] { "EVADE" }, third: new[] { "DODGES", "EVADES" }, past: new[] { "DODGED", "EVADED" }, progressive: new[] { "DODGING", "EVADING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("JUMP", BattleActionRole.Defense, 2, 0, 1f, 4.5f, "Jump", "MOVEMENT", "DEFENSE", movementVerb: true, movementProfile: "leap", aliases: new[] { "LEAP" }, third: new[] { "JUMPS", "LEAPS" }, past: new[] { "JUMPED", "LEAPED" }, progressive: new[] { "JUMPING", "LEAPING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("HIDE", BattleActionRole.Defense, 2, 0, 1f, 4f, "Move", "DEFENSE", "UTILITY", movementVerb: true, movementProfile: "cover", aliases: new[] { "COVER", "DUCK" }, third: new[] { "HIDES", "COVERS", "DUCKS" }, past: new[] { "HID", "COVERED", "DUCKED" }, progressive: new[] { "HIDING", "COVERING", "DUCKING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("FLY", BattleActionRole.Dodge, 3, 0, 1f, 7f, "Move", "MOVEMENT", "BIRD", movementVerb: true, movementProfile: "glide", aliases: new[] { "SOAR" }, third: new[] { "FLIES", "SOARS" }, past: new[] { "FLEW", "SOARED" }, progressive: new[] { "FLYING", "SOARING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("SWIM", BattleActionRole.Defense, 2, 0, 1f, 5f, "Move", "MOVEMENT", "AQUATIC", movementVerb: true, movementProfile: "water", aliases: new[] { "PADDLE" }, third: new[] { "SWIMS", "PADDLES" }, past: new[] { "SWAM", "PADDLED" }, progressive: new[] { "SWIMMING", "PADDLING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("ROLL", BattleActionRole.Dodge, 2, 1, 0.96f, 4.2f, "Move", "MOVEMENT", "OBJECT", movementVerb: true, movementProfile: "roll", aliases: new[] { "TUMBLE" }, third: new[] { "ROLLS", "TUMBLES" }, past: new[] { "ROLLED", "TUMBLED" }, progressive: new[] { "ROLLING", "TUMBLING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("DROP", BattleActionRole.Offense, 2, 2, 0.88f, 3.2f, "Attack", "UTILITY", "OBJECT", third: new[] { "DROPS" }, past: new[] { "DROPPED" }, progressive: new[] { "DROPPING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("BOUNCE", BattleActionRole.Dodge, 2, 1, 0.95f, 4f, "Move", "MOVEMENT", "OBJECT", movementVerb: true, movementProfile: "bounce", third: new[] { "BOUNCES" }, past: new[] { "BOUNCED" }, progressive: new[] { "BOUNCING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("DRIFT", BattleActionRole.Defense, 2, 1, 1f, 4.8f, "Move", "MOVEMENT", "AQUATIC", movementVerb: true, movementProfile: "drift", aliases: new[] { "COAST" }, third: new[] { "DRIFTS", "COASTS" }, past: new[] { "DRIFTED", "COASTED" }, progressive: new[] { "DRIFTING", "COASTING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("ROCK", BattleActionRole.Defense, 2, 1, 1f, 3.5f, "Move", "UTILITY", "VEHICLE", movementVerb: true, movementProfile: "brace", third: new[] { "ROCKS" }, past: new[] { "ROCKED" }, progressive: new[] { "ROCKING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CHARGE", BattleActionRole.Offense, 3, 3, 0.8f, 3.4f, "Attack", "OFFENSE", "ANIMAL", aliases: new[] { "RUSH" }, third: new[] { "CHARGES", "RUSHES" }, past: new[] { "CHARGED", "RUSHED" }, progressive: new[] { "CHARGING", "RUSHING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("THROW", BattleActionRole.Offense, 3, 2, 0.86f, 4.1f, "Attack", "UTILITY", "PERSON", aliases: new[] { "TOSS" }, third: new[] { "THROWS", "TOSSES" }, past: new[] { "THREW", "TOSSED" }, progressive: new[] { "THROWING", "TOSSING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("STING", BattleActionRole.Offense, 2, 2, 0.93f, 2.4f, "Attack", "OFFENSE", "ANIMAL", third: new[] { "STINGS" }, past: new[] { "STUNG" }, progressive: new[] { "STINGING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("GLOW", BattleActionRole.Defense, 2, 0, 1f, 3f, "Move", "UTILITY", "NATURE", aliases: new[] { "SHIMMER" }, third: new[] { "GLOWS", "SHIMMERS" }, past: new[] { "GLOWED", "SHIMMERED" }, progressive: new[] { "GLOWING", "SHIMMERING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("SHAKE", BattleActionRole.Defense, 2, 0, 1f, 3.2f, "Move", "UTILITY", "NATURE", movementVerb: true, movementProfile: "shake", aliases: new[] { "TREMBLE" }, third: new[] { "SHAKES", "TREMBLES" }, past: new[] { "SHOOK", "TREMBLED" }, progressive: new[] { "SHAKING", "TREMBLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("GLITTER", BattleActionRole.Defense, 2, 0, 1f, 3f, "Move", "UTILITY", "FANTASY", aliases: new[] { "SPARKLE" }, third: new[] { "GLITTERS", "SPARKLES" }, past: new[] { "GLITTERED", "SPARKLED" }, progressive: new[] { "GLITTERING", "SPARKLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("DIG", BattleActionRole.Offense, 2, 2, 0.9f, 2.8f, "Attack", "UTILITY", "ANIMAL", aliases: new[] { "BURROW" }, third: new[] { "DIGS", "BURROWS" }, past: new[] { "DUG", "BURROWED" }, progressive: new[] { "DIGGING", "BURROWING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CHASE", BattleActionRole.Offense, 2, 2, 0.91f, 3.5f, "Attack", "OFFENSE", "ANIMAL", aliases: new[] { "FOLLOW", "HUNT" }, third: new[] { "CHASES", "FOLLOWS", "HUNTS" }, past: new[] { "CHASED", "FOLLOWED", "HUNTED" }, progressive: new[] { "CHASING", "FOLLOWING", "HUNTING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("SNIFF", BattleActionRole.Defense, 2, 0, 1f, 2.5f, "Move", "UTILITY", "ANIMAL", aliases: new[] { "SMELL" }, third: new[] { "SNIFFS", "SMELLS" }, past: new[] { "SNIFFED", "SMELLED" }, progressive: new[] { "SNIFFING", "SMELLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CLIMB", BattleActionRole.Defense, 2, 0, 1f, 4f, "Move", "MOVEMENT", "ANIMAL", aliases: new[] { "SCALE" }, third: new[] { "CLIMBS", "SCALES" }, past: new[] { "CLIMBED", "SCALED" }, progressive: new[] { "CLIMBING", "SCALING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("CRAWL", BattleActionRole.Defense, 2, 0, 1f, 3.4f, "Move", "MOVEMENT", "ANIMAL", aliases: new[] { "CREEP" }, third: new[] { "CRAWLS", "CREEPS" }, past: new[] { "CRAWLED", "CREPT" }, progressive: new[] { "CRAWLING", "CREEPING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("GLIDE", BattleActionRole.Dodge, 2, 0, 1f, 5.8f, "Move", "MOVEMENT", "BIRD", movementVerb: true, movementProfile: "glide", third: new[] { "GLIDES" }, past: new[] { "GLIDED" }, progressive: new[] { "GLIDING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("DIVE", BattleActionRole.Dodge, 2, 1, 0.95f, 5f, "Move", "MOVEMENT", "AQUATIC", movementVerb: true, movementProfile: "dive", aliases: new[] { "PLUNGE" }, third: new[] { "DIVES", "PLUNGES" }, past: new[] { "DOVE", "PLUNGED" }, progressive: new[] { "DIVING", "PLUNGING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("SPLASH", BattleActionRole.Offense, 2, 1, 0.92f, 3.5f, "Attack", "OFFENSE", "AQUATIC", third: new[] { "SPLASHES" }, past: new[] { "SPLASHED" }, progressive: new[] { "SPLASHING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("FLOAT", BattleActionRole.Defense, 2, 0, 1f, 4.4f, "Move", "MOVEMENT", "AQUATIC", movementVerb: true, movementProfile: "float", aliases: new[] { "BOB" }, third: new[] { "FLOATS", "BOBS" }, past: new[] { "FLOATED", "BOBBED" }, progressive: new[] { "FLOATING", "BOBBING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("DRIVE", BattleActionRole.Dodge, 2, 1, 0.95f, 5.5f, "Move", "MOVEMENT", "VEHICLE", movementVerb: true, movementProfile: "drive", aliases: new[] { "RIDE" }, third: new[] { "DRIVES", "RIDES" }, past: new[] { "DROVE", "RODE" }, progressive: new[] { "DRIVING", "RIDING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("TURN", BattleActionRole.Defense, 2, 0, 1f, 3.5f, "Move", "MOVEMENT", "VEHICLE", movementVerb: true, movementProfile: "turn", aliases: new[] { "VEER" }, third: new[] { "TURNS", "VEERS" }, past: new[] { "TURNED", "VEERED" }, progressive: new[] { "TURNING", "VEERING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("SPIN", BattleActionRole.Dodge, 2, 1, 0.96f, 3.8f, "Move", "MOVEMENT", "OBJECT", movementVerb: true, movementProfile: "spin", aliases: new[] { "WHIRL" }, third: new[] { "SPINS", "WHIRLS" }, past: new[] { "SPUN", "WHIRLED" }, progressive: new[] { "SPINNING", "WHIRLING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("SLIDE", BattleActionRole.Defense, 2, 0, 1f, 4.1f, "Move", "MOVEMENT", "OBJECT", movementVerb: true, movementProfile: "slide", aliases: new[] { "SKID" }, third: new[] { "SLIDES", "SKIDS" }, past: new[] { "SLID", "SKIDDED" }, progressive: new[] { "SLIDING", "SKIDDING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("OPEN", BattleActionRole.Defense, 2, 0, 1f, 2.5f, "Move", "UTILITY", "OBJECT", aliases: new[] { "UNLOCK", "UNLATCH" }, third: new[] { "OPENS", "UNLOCKS", "UNLATCHES" }, past: new[] { "OPENED", "UNLOCKED", "UNLATCHED" }, progressive: new[] { "OPENING", "UNLOCKING", "UNLATCHING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CLOSE", BattleActionRole.Defense, 2, 0, 1f, 2.5f, "Move", "UTILITY", "OBJECT", aliases: new[] { "SHUT" }, third: new[] { "CLOSES", "SHUTS" }, past: new[] { "CLOSED", "SHUT" }, progressive: new[] { "CLOSING", "SHUTTING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CARRY", BattleActionRole.Defense, 2, 1, 1f, 3f, "Move", "UTILITY", "PERSON", aliases: new[] { "HOLD", "BRING", "HAUL" }, third: new[] { "CARRIES", "HOLDS", "BRINGS", "HAULS" }, past: new[] { "CARRIED", "HELD", "BROUGHT", "HAULED" }, progressive: new[] { "CARRYING", "HOLDING", "BRINGING", "HAULING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("WAVE", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", third: new[] { "WAVES" }, past: new[] { "WAVED" }, progressive: new[] { "WAVING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CALL", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "SHOUT", "YELL", "CRY" }, third: new[] { "CALLS", "SHOUTS", "YELLS", "CRIES" }, past: new[] { "CALLED", "SHOUTED", "YELLED", "CRIED" }, progressive: new[] { "CALLING", "SHOUTING", "YELLING", "CRYING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("LOOK", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "SEE", "STARE", "GLANCE", "PEEK" }, third: new[] { "LOOKS", "SEES", "STARES", "GLANCES", "PEEKS" }, past: new[] { "LOOKED", "SAW", "STARED", "GLANCED", "PEEKED" }, progressive: new[] { "LOOKING", "SEEING", "STARING", "GLANCING", "PEEKING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("SING", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "HUM", "CHIRP" }, third: new[] { "SINGS", "HUMS", "CHIRPS" }, past: new[] { "SANG", "HUMMED", "CHIRPED" }, progressive: new[] { "SINGING", "HUMMING", "CHIRPING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("DANCE", BattleActionRole.Dodge, 2, 0, 1f, 3.5f, "Move", "MOVEMENT", "PERSON", movementVerb: true, movementProfile: "dance", aliases: new[] { "TWIRL" }, third: new[] { "DANCES", "TWIRLS" }, past: new[] { "DANCED", "TWIRLED" }, progressive: new[] { "DANCING", "TWIRLING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("READ", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "STUDY" }, third: new[] { "READS", "STUDIES" }, past: new[] { "READ", "STUDIED" }, progressive: new[] { "READING", "STUDYING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("WRITE", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "PRINT", "SCRIBBLE" }, third: new[] { "WRITES", "PRINTS", "SCRIBBLES" }, past: new[] { "WROTE", "PRINTED", "SCRIBBLED" }, progressive: new[] { "WRITING", "PRINTING", "SCRIBBLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("COUNT", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "TALLY" }, third: new[] { "COUNTS", "TALLIES" }, past: new[] { "COUNTED", "TALLIED" }, progressive: new[] { "COUNTING", "TALLYING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("GROW", BattleActionRole.Defense, 2, 0, 1f, 2.8f, "Move", "UTILITY", "NATURE", aliases: new[] { "SPROUT" }, third: new[] { "GROWS", "SPROUTS" }, past: new[] { "GREW", "SPROUTED" }, progressive: new[] { "GROWING", "SPROUTING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("BLOOM", BattleActionRole.Defense, 2, 0, 1f, 2.8f, "Move", "UTILITY", "NATURE", aliases: new[] { "FLOWER" }, third: new[] { "BLOOMS", "FLOWERS" }, past: new[] { "BLOOMED", "FLOWERED" }, progressive: new[] { "BLOOMING", "FLOWERING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("FALL", BattleActionRole.Offense, 2, 1, 0.9f, 3f, "Attack", "UTILITY", "NATURE", third: new[] { "FALLS" }, past: new[] { "FELL" }, progressive: new[] { "FALLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("SWAY", BattleActionRole.Dodge, 2, 0, 1f, 3f, "Move", "MOVEMENT", "NATURE", movementVerb: true, movementProfile: "sway", third: new[] { "SWAYS" }, past: new[] { "SWAYED" }, progressive: new[] { "SWAYING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("SHINE", BattleActionRole.Defense, 2, 0, 1f, 3f, "Move", "UTILITY", "NATURE", aliases: new[] { "GLEAM" }, third: new[] { "SHINES", "GLEAMS" }, past: new[] { "SHONE", "GLEAMED" }, progressive: new[] { "SHINING", "GLEAMING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("PLAY", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "OBJECT", aliases: new[] { "PERFORM" }, third: new[] { "PLAYS", "PERFORMS" }, past: new[] { "PLAYED", "PERFORMED" }, progressive: new[] { "PLAYING", "PERFORMING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("RING", BattleActionRole.Offense, 2, 1, 0.92f, 2.8f, "Attack", "UTILITY", "OBJECT", aliases: new[] { "CHIME" }, third: new[] { "RINGS", "CHIMES" }, past: new[] { "RANG", "CHIMED" }, progressive: new[] { "RINGING", "CHIMING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("BLOCK", BattleActionRole.Defense, 2, 0, 1f, 2.8f, "Move", "DEFENSE", "UTILITY", aliases: new[] { "DEFEND", "GUARD", "PROTECT", "SHIELD" }, third: new[] { "BLOCKS", "DEFENDS", "GUARDS", "PROTECTS", "SHIELDS" }, past: new[] { "BLOCKED", "DEFENDED", "GUARDED", "PROTECTED", "SHIELDED" }, progressive: new[] { "BLOCKING", "DEFENDING", "GUARDING", "PROTECTING", "SHIELDING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("SHIFT", BattleActionRole.Dodge, 2, 0, 1f, 3.6f, "Move", "MOVEMENT", "UTILITY", movementVerb: true, movementProfile: "shift", third: new[] { "SHIFTS" }, past: new[] { "SHIFTED" }, progressive: new[] { "SHIFTING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("BUMP", BattleActionRole.Offense, 2, 1, 0.94f, 2.6f, "Attack", "UTILITY", "OBJECT", aliases: new[] { "NUDGE" }, third: new[] { "BUMPS", "NUDGES" }, past: new[] { "BUMPED", "NUDGED" }, progressive: new[] { "BUMPING", "NUDGING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("PUSH", BattleActionRole.Offense, 2, 1, 0.9f, 3f, "Attack", "UTILITY", "OBJECT", aliases: new[] { "SHOVE" }, third: new[] { "PUSHES", "SHOVES" }, past: new[] { "PUSHED", "SHOVED" }, progressive: new[] { "PUSHING", "SHOVING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("PULL", BattleActionRole.Defense, 2, 1, 0.9f, 3f, "Move", "UTILITY", "OBJECT", aliases: new[] { "TUG" }, third: new[] { "PULLS", "TUGS" }, past: new[] { "PULLED", "TUGGED" }, progressive: new[] { "PULLING", "TUGGING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("LISTEN", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "HEAR" }, third: new[] { "LISTENS", "HEARS" }, past: new[] { "LISTENED", "HEARD" }, progressive: new[] { "LISTENING", "HEARING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("BLINK", BattleActionRole.Dodge, 2, 0, 1f, 2.2f, "Move", "DEFENSE", "BODY", movementVerb: true, movementProfile: "blink", third: new[] { "BLINKS" }, past: new[] { "BLINKED" }, progressive: new[] { "BLINKING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("GLARE", BattleActionRole.Offense, 2, 1, 0.95f, 3f, "Attack", "UTILITY", "BODY", third: new[] { "GLARES" }, past: new[] { "GLARED" }, progressive: new[] { "GLARING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("KICK", BattleActionRole.Offense, 2, 2, 0.91f, 2.7f, "Attack", "OFFENSE", "BODY", third: new[] { "KICKS" }, past: new[] { "KICKED" }, progressive: new[] { "KICKING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("HOP", BattleActionRole.Dodge, 2, 0, 1f, 3.3f, "Move", "MOVEMENT", "BODY", movementVerb: true, movementProfile: "hop", third: new[] { "HOPS" }, past: new[] { "HOPPED" }, progressive: new[] { "HOPPING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("TICK", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "OBJECT", third: new[] { "TICKS" }, past: new[] { "TICKED" }, progressive: new[] { "TICKING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("LAND", BattleActionRole.Defense, 2, 0, 1f, 4.2f, "Move", "MOVEMENT", "BIRD", movementVerb: true, movementProfile: "land", aliases: new[] { "PERCH" }, third: new[] { "LANDS", "PERCHES" }, past: new[] { "LANDED", "PERCHED" }, progressive: new[] { "LANDING", "PERCHING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("HOVER", BattleActionRole.Dodge, 2, 0, 1f, 4.8f, "Move", "MOVEMENT", "BIRD", movementVerb: true, movementProfile: "hover", third: new[] { "HOVERS" }, past: new[] { "HOVERED" }, progressive: new[] { "HOVERING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("POUR", BattleActionRole.Offense, 2, 1, 0.9f, 3f, "Attack", "UTILITY", "OBJECT", aliases: new[] { "STREAM" }, third: new[] { "POURS", "STREAMS" }, past: new[] { "POURED", "STREAMED" }, progressive: new[] { "POURING", "STREAMING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("FILL", BattleActionRole.Defense, 2, 0, 1f, 2.5f, "Move", "UTILITY", "OBJECT", third: new[] { "FILLS" }, past: new[] { "FILLED" }, progressive: new[] { "FILLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("EMPTY", BattleActionRole.Offense, 2, 1, 0.92f, 2.5f, "Attack", "UTILITY", "OBJECT", aliases: new[] { "DRAIN" }, third: new[] { "EMPTIES", "DRAINS" }, past: new[] { "EMPTIED", "DRAINED" }, progressive: new[] { "EMPTYING", "DRAINING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("STIR", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "OBJECT", aliases: new[] { "MIX" }, third: new[] { "STIRS", "MIXES" }, past: new[] { "STIRRED", "MIXED" }, progressive: new[] { "STIRRING", "MIXING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("SPILL", BattleActionRole.Offense, 2, 1, 0.9f, 2.8f, "Attack", "UTILITY", "OBJECT", aliases: new[] { "SLOSH" }, third: new[] { "SPILLS", "SLOSHES" }, past: new[] { "SPILLED", "SLOSHED" }, progressive: new[] { "SPILLING", "SLOSHING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("MELT", BattleActionRole.Offense, 2, 1, 0.92f, 2.5f, "Attack", "UTILITY", "NATURE", aliases: new[] { "THAW" }, third: new[] { "MELTS", "THAWS" }, past: new[] { "MELTED", "THAWED" }, progressive: new[] { "MELTING", "THAWING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("DRIP", BattleActionRole.Offense, 2, 1, 0.92f, 2.8f, "Attack", "UTILITY", "NATURE", aliases: new[] { "OOZE" }, third: new[] { "DRIPS", "OOZES" }, past: new[] { "DRIPPED", "OOZED" }, progressive: new[] { "DRIPPING", "OOZING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("STICK", BattleActionRole.Defense, 2, 1, 1f, 2.5f, "Move", "UTILITY", "OBJECT", aliases: new[] { "CLING" }, third: new[] { "STICKS", "CLINGS" }, past: new[] { "STUCK", "CLUNG" }, progressive: new[] { "STICKING", "CLINGING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CRACK", BattleActionRole.Offense, 2, 2, 0.92f, 2.8f, "Attack", "OFFENSE", "OBJECT", aliases: new[] { "SPLIT" }, third: new[] { "CRACKS", "SPLITS" }, past: new[] { "CRACKED", "SPLIT" }, progressive: new[] { "CRACKING", "SPLITTING" }, allowedAdverbs: TagBasedAdverbs("offense")),
            BuildVerb("NAP", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "DOZE" }, third: new[] { "NAPS", "DOZES" }, past: new[] { "NAPPED", "DOZED" }, progressive: new[] { "NAPPING", "DOZING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("SIT", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", third: new[] { "SITS" }, past: new[] { "SAT" }, progressive: new[] { "SITTING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("SLEEP", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "SLUMBER" }, third: new[] { "SLEEPS", "SLUMBERS" }, past: new[] { "SLEPT", "SLUMBERED" }, progressive: new[] { "SLEEPING", "SLUMBERING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("HONK", BattleActionRole.Offense, 2, 1, 0.92f, 3.2f, "Attack", "UTILITY", "VEHICLE", aliases: new[] { "BEEP" }, third: new[] { "HONKS", "BEEPS" }, past: new[] { "HONKED", "BEEPED" }, progressive: new[] { "HONKING", "BEEPING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("PARK", BattleActionRole.Defense, 2, 0, 1f, 2.8f, "Move", "MOVEMENT", "VEHICLE", movementVerb: true, movementProfile: "park", aliases: new[] { "STOP" }, third: new[] { "PARKS", "STOPS" }, past: new[] { "PARKED", "STOPPED" }, progressive: new[] { "PARKING", "STOPPING" }, allowedAdverbs: TagBasedAdverbs("movement")),
            BuildVerb("GRAZE", BattleActionRole.Defense, 2, 1, 1f, 2.5f, "Move", "UTILITY", "ANIMAL", aliases: new[] { "NIBBLE" }, third: new[] { "GRAZES", "NIBBLES" }, past: new[] { "GRAZED", "NIBBLED" }, progressive: new[] { "GRAZING", "NIBBLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("REST", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "LOUNGE" }, third: new[] { "RESTS", "LOUNGES" }, past: new[] { "RESTED", "LOUNGED" }, progressive: new[] { "RESTING", "LOUNGING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("POINT", BattleActionRole.Defense, 2, 0, 1f, 3f, "Move", "UTILITY", "PERSON", aliases: new[] { "AIM" }, third: new[] { "POINTS", "AIMS" }, past: new[] { "POINTED", "AIMED" }, progressive: new[] { "POINTING", "AIMING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("TAP", BattleActionRole.Offense, 2, 1, 0.95f, 2.4f, "Attack", "UTILITY", "OBJECT", aliases: new[] { "PAT" }, third: new[] { "TAPS", "PATS" }, past: new[] { "TAPPED", "PATTED" }, progressive: new[] { "TAPPING", "PATTING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CHEW", BattleActionRole.Offense, 2, 1, 0.94f, 2.2f, "Attack", "UTILITY", "ANIMAL", aliases: new[] { "MUNCH" }, third: new[] { "CHEWS", "MUNCHES" }, past: new[] { "CHEWED", "MUNCHED" }, progressive: new[] { "CHEWING", "MUNCHING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("LICK", BattleActionRole.Defense, 2, 1, 1f, 2.1f, "Move", "UTILITY", "ANIMAL", aliases: new[] { "TASTE" }, third: new[] { "LICKS", "TASTES" }, past: new[] { "LICKED", "TASTED" }, progressive: new[] { "LICKING", "TASTING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("EAT", BattleActionRole.Defense, 2, 1, 1f, 2.2f, "Move", "UTILITY", "ANIMAL", aliases: new[] { "DINE" }, third: new[] { "EATS", "DINES" }, past: new[] { "ATE", "DINED" }, progressive: new[] { "EATING", "DINING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("DRINK", BattleActionRole.Defense, 2, 1, 1f, 2.2f, "Move", "UTILITY", "ANIMAL", aliases: new[] { "SIP" }, third: new[] { "DRINKS", "SIPS" }, past: new[] { "DRANK", "SIPPED" }, progressive: new[] { "DRINKING", "SIPPING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("ASK", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "QUESTION", "INQUIRE" }, third: new[] { "ASKS", "QUESTIONS", "INQUIRES" }, past: new[] { "ASKED", "QUESTIONED", "INQUIRED" }, progressive: new[] { "ASKING", "QUESTIONING", "INQUIRING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("ANSWER", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "REPLY", "RESPOND" }, third: new[] { "ANSWERS", "REPLIES", "RESPONDS" }, past: new[] { "ANSWERED", "REPLIED", "RESPONDED" }, progressive: new[] { "ANSWERING", "REPLYING", "RESPONDING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("EXPLAIN", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "CLARIFY" }, third: new[] { "EXPLAINS", "CLARIFIES" }, past: new[] { "EXPLAINED", "CLARIFIED" }, progressive: new[] { "EXPLAINING", "CLARIFYING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("DESCRIBE", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "TELL" }, third: new[] { "DESCRIBES", "TELLS" }, past: new[] { "DESCRIBED", "TOLD" }, progressive: new[] { "DESCRIBING", "TELLING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("LEARN", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", third: new[] { "LEARNS" }, past: new[] { "LEARNED", "LEARNT" }, progressive: new[] { "LEARNING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("THINK", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "PONDER" }, third: new[] { "THINKS", "PONDERS" }, past: new[] { "THOUGHT", "PONDERED" }, progressive: new[] { "THINKING", "PONDERING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("REMEMBER", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "RECALL" }, third: new[] { "REMEMBERS", "RECALLS" }, past: new[] { "REMEMBERED", "RECALLED" }, progressive: new[] { "REMEMBERING", "RECALLING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("SOLVE", BattleActionRole.Defense, 2, 1, 1f, 2.4f, "Move", "UTILITY", "PERSON", aliases: new[] { "FIX" }, third: new[] { "SOLVES", "FIXES" }, past: new[] { "SOLVED", "FIXED" }, progressive: new[] { "SOLVING", "FIXING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("COMPARE", BattleActionRole.Defense, 2, 0, 1f, 2f, "Move", "UTILITY", "PERSON", aliases: new[] { "MATCH" }, third: new[] { "COMPARES", "MATCHES" }, past: new[] { "COMPARED", "MATCHED" }, progressive: new[] { "COMPARING", "MATCHING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("MEASURE", BattleActionRole.Defense, 2, 0, 1f, 2.4f, "Move", "UTILITY", "PERSON", aliases: new[] { "CHECK" }, third: new[] { "MEASURES", "CHECKS" }, past: new[] { "MEASURED", "CHECKED" }, progressive: new[] { "MEASURING", "CHECKING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("OBSERVE", BattleActionRole.Defense, 2, 0, 1f, 2.6f, "Move", "UTILITY", "PERSON", aliases: new[] { "WATCH" }, third: new[] { "OBSERVES", "WATCHES" }, past: new[] { "OBSERVED", "WATCHED" }, progressive: new[] { "OBSERVING", "WATCHING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("NOTICE", BattleActionRole.Defense, 2, 0, 1f, 2.4f, "Move", "UTILITY", "PERSON", aliases: new[] { "SPOT" }, third: new[] { "NOTICES", "SPOTS" }, past: new[] { "NOTICED", "SPOTTED" }, progressive: new[] { "NOTICING", "SPOTTING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("FIND", BattleActionRole.Defense, 2, 1, 1f, 2.6f, "Move", "UTILITY", "PERSON", aliases: new[] { "LOCATE" }, third: new[] { "FINDS", "LOCATES" }, past: new[] { "FOUND", "LOCATED" }, progressive: new[] { "FINDING", "LOCATING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("DISCOVER", BattleActionRole.Defense, 2, 1, 1f, 2.6f, "Move", "UTILITY", "PERSON", aliases: new[] { "UNEARTH" }, third: new[] { "DISCOVERS", "UNEARTHS" }, past: new[] { "DISCOVERED", "UNEARTHED" }, progressive: new[] { "DISCOVERING", "UNEARTHING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CHOOSE", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "PERSON", aliases: new[] { "PICK", "SELECT" }, third: new[] { "CHOOSES", "PICKS", "SELECTS" }, past: new[] { "CHOSE", "PICKED", "SELECTED" }, progressive: new[] { "CHOOSING", "PICKING", "SELECTING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("DECIDE", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "PERSON", third: new[] { "DECIDES" }, past: new[] { "DECIDED" }, progressive: new[] { "DECIDING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("DRAW", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "PERSON", aliases: new[] { "SKETCH" }, third: new[] { "DRAWS", "SKETCHES" }, past: new[] { "DREW", "SKETCHED" }, progressive: new[] { "DRAWING", "SKETCHING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("COLOR", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "PERSON", aliases: new[] { "PAINT" }, third: new[] { "COLORS", "PAINTS" }, past: new[] { "COLORED", "PAINTED" }, progressive: new[] { "COLORING", "PAINTING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("HELP", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "PERSON", aliases: new[] { "ASSIST" }, third: new[] { "HELPS", "ASSISTS" }, past: new[] { "HELPED", "ASSISTED" }, progressive: new[] { "HELPING", "ASSISTING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("SHARE", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "PERSON", aliases: new[] { "GIVE" }, third: new[] { "SHARES", "GIVES" }, past: new[] { "SHARED", "GAVE" }, progressive: new[] { "SHARING", "GIVING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("TEACH", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "PERSON", aliases: new[] { "SHOW" }, third: new[] { "TEACHES", "SHOWS" }, past: new[] { "TAUGHT", "SHOWED" }, progressive: new[] { "TEACHING", "SHOWING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("CLEAN", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "OBJECT", aliases: new[] { "TIDY" }, third: new[] { "CLEANS", "TIDIES" }, past: new[] { "CLEANED", "TIDIED" }, progressive: new[] { "CLEANING", "TIDYING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("WIPE", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "OBJECT", aliases: new[] { "SWAB" }, third: new[] { "WIPES", "SWABS" }, past: new[] { "WIPED", "SWABBED" }, progressive: new[] { "WIPING", "SWABBING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("WASH", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "OBJECT", aliases: new[] { "RINSE" }, third: new[] { "WASHES", "RINSES" }, past: new[] { "WASHED", "RINSED" }, progressive: new[] { "WASHING", "RINSING" }, allowedAdverbs: TagBasedAdverbs("defense")),
            BuildVerb("FOLD", BattleActionRole.Defense, 2, 0, 1f, 2.2f, "Move", "UTILITY", "OBJECT", aliases: new[] { "CREASE" }, third: new[] { "FOLDS", "CREASES" }, past: new[] { "FOLDED", "CREASED" }, progressive: new[] { "FOLDING", "CREASING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("STACK", BattleActionRole.Defense, 2, 0, 1f, 2.4f, "Move", "UTILITY", "OBJECT", aliases: new[] { "PILE" }, third: new[] { "STACKS", "PILES" }, past: new[] { "STACKED", "PILED" }, progressive: new[] { "STACKING", "PILING" }, allowedAdverbs: TagBasedAdverbs("utility")),
            BuildVerb("SORT", BattleActionRole.Defense, 2, 0, 1f, 2.4f, "Move", "UTILITY", "OBJECT", aliases: new[] { "ORDER" }, third: new[] { "SORTS", "ORDERS" }, past: new[] { "SORTED", "ORDERED" }, progressive: new[] { "SORTING", "ORDERING" }, allowedAdverbs: TagBasedAdverbs("utility")),
        };
    }

    static VerbActionDefinition BuildVerb(
        string verb,
        BattleActionRole role,
        int ppCost,
        int power,
        float accuracy,
        float range,
        string animationTrigger,
        string tagA,
        string tagB,
        bool movementVerb = false,
        string movementProfile = "advance",
        IEnumerable<string> aliases = null,
        IEnumerable<string> third = null,
        IEnumerable<string> past = null,
        IEnumerable<string> progressive = null,
        List<string> allowedAdverbs = null)
    {
        var conjugation = new VerbConjugationRecord
        {
            bare = verb,
            pluralPresentForms = new List<string> { verb },
            thirdPersonSingularForms = ToList(third),
            pastTenseForms = ToList(past),
            progressiveForms = ToList(progressive),
        };

        return new VerbActionDefinition
        {
            verb = verb,
            aliases = ToList(aliases),
            verbTags = new List<string> { tagA, tagB },
            allowedAdverbs = allowedAdverbs ?? new List<string>(),
            conjugation = conjugation,
            thirdPersonSingularForms = ToList(third),
            pastTenseForms = ToList(past),
            progressiveForms = ToList(progressive),
            role = role,
            ppCost = ppCost,
            power = power,
            accuracy = accuracy,
            range = range,
            tacticalRangeCells = ResolveTacticalRangeCells(verb),
            tacticalMovementCells = ResolveTacticalMovementCells(verb, movementVerb),
            tacticalDamageMultiplier = ResolveTacticalDamageMultiplier(verb),
            cooldownSeconds = 0.5f,
            movementVerb = movementVerb,
            movementProfile = movementProfile,
            animationTrigger = animationTrigger,
        };
    }

    static int ResolveTacticalRangeCells(string verb)
    {
        switch (CreaturePhraseUtility.NormalizeToken(verb))
        {
            case "THROW":
            case "GLARE":
                return 3;
            case "SPLASH":
            case "POUR":
            case "HONK":
            case "RING":
                return 2;
            default:
                return 1;
        }
    }

    static float ResolveTacticalDamageMultiplier(string verb)
    {
        switch (CreaturePhraseUtility.NormalizeToken(verb))
        {
            case "THROW":
            case "GLARE":
                return 0.55f;
            case "SPLASH":
            case "POUR":
            case "HONK":
            case "RING":
                return 0.75f;
            default:
                return 1f;
        }
    }

    static int ResolveTacticalMovementCells(string verb, bool movementVerb)
    {
        if (!movementVerb)
            return 0;

        switch (CreaturePhraseUtility.NormalizeToken(verb))
        {
            case "WALK":
            case "DODGE":
            case "HIDE":
            case "PARK":
            case "LAND":
                return 1;
            case "FLY":
            case "GLIDE":
            case "DRIVE":
                return 3;
            default:
                return 2;
        }
    }

    static ModifierDefinition BuildModifier(
        string modifier,
        ModifierGrammarRole role,
        IEnumerable<string> semanticTags = null,
        IEnumerable<string> aliases = null,
        IEnumerable<string> allowedNounTags = null,
        IEnumerable<string> allowedVerbTags = null,
        float maxHpMultiplier = 1f,
        float attackMultiplier = 1f,
        float defenseMultiplier = 1f,
        float speedMultiplier = 1f,
        float accuracyMultiplier = 1f,
        float powerMultiplier = 1f,
        float evasionMultiplier = 1f,
        float ppCostMultiplier = 1f)
    {
        return new ModifierDefinition
        {
            modifier = modifier,
            role = role,
            semanticTags = ToList(semanticTags),
            aliases = ToList(aliases),
            allowedNounTags = ToList(allowedNounTags),
            allowedVerbTags = ToList(allowedVerbTags),
            maxHpMultiplier = maxHpMultiplier,
            attackMultiplier = attackMultiplier,
            defenseMultiplier = defenseMultiplier,
            speedMultiplier = speedMultiplier,
            accuracyMultiplier = accuracyMultiplier,
            powerMultiplier = powerMultiplier,
            evasionMultiplier = evasionMultiplier,
            ppCostMultiplier = ppCostMultiplier,
        };
    }

    static List<ModifierDefinition> BuildDefaultModifiers()
    {
        return new List<ModifierDefinition>
        {
            BuildModifier("BIG", ModifierGrammarRole.Adjective, semanticTags: new[] { "POWER", "HP" }, aliases: new[] { "LARGE", "HUGE" }, maxHpMultiplier: 1.25f, attackMultiplier: 1.35f, defenseMultiplier: 1.1f, speedMultiplier: 0.75f),
            BuildModifier("SMALL", ModifierGrammarRole.Adjective, semanticTags: new[] { "SPEED", "EVADE" }, aliases: new[] { "TINY", "LITTLE" }, maxHpMultiplier: 0.8f, attackMultiplier: 0.75f, defenseMultiplier: 0.85f, speedMultiplier: 1.3f, evasionMultiplier: 1.15f),
            BuildModifier("TOUGH", ModifierGrammarRole.Adjective, semanticTags: new[] { "DEFENSE" }, aliases: new[] { "STURDY", "SOLID" }, maxHpMultiplier: 1.15f, attackMultiplier: 0.95f, defenseMultiplier: 1.35f, speedMultiplier: 0.85f),
            BuildModifier("SWIFT", ModifierGrammarRole.Adjective, semanticTags: new[] { "SPEED" }, aliases: new[] { "QUICK" }, allowedNounTags: new[] { "ANIMAL", "BIRD", "AQUATIC", "OBJECT", "FANTASY" }, maxHpMultiplier: 0.9f, defenseMultiplier: 0.9f, speedMultiplier: 1.45f),
            BuildModifier("BRAVE", ModifierGrammarRole.Adjective, semanticTags: new[] { "POWER" }, aliases: new[] { "BOLD", "FIERCE" }, allowedNounTags: new[] { "ANIMAL", "PERSON", "FANTASY" }, maxHpMultiplier: 1.05f, attackMultiplier: 1.2f, defenseMultiplier: 1.05f, speedMultiplier: 0.95f),
            BuildModifier("GENTLE", ModifierGrammarRole.Adjective, semanticTags: new[] { "CAREFUL" }, aliases: new[] { "CALM", "MILD" }, attackMultiplier: 0.85f, defenseMultiplier: 1.15f, speedMultiplier: 1.05f),
            BuildModifier("BRIGHT", ModifierGrammarRole.Adjective, semanticTags: new[] { "PRECISION" }, aliases: new[] { "SHINY", "GLOWING" }, defenseMultiplier: 0.95f, speedMultiplier: 1.1f),
            BuildModifier("HOT", ModifierGrammarRole.Adjective, semanticTags: new[] { "POWER" }, aliases: new[] { "WARM" }, attackMultiplier: 1.1f, defenseMultiplier: 0.95f, speedMultiplier: 1.05f),
            BuildModifier("COLD", ModifierGrammarRole.Adjective, semanticTags: new[] { "CONTROL" }, aliases: new[] { "COOL", "CHILLY" }, attackMultiplier: 0.95f, defenseMultiplier: 1.1f, speedMultiplier: 0.98f),
            BuildModifier("LOUD", ModifierGrammarRole.Adjective, semanticTags: new[] { "POWER" }, aliases: new[] { "NOISY" }, attackMultiplier: 1.12f, defenseMultiplier: 0.95f, speedMultiplier: 1.02f),
            BuildModifier("QUIET", ModifierGrammarRole.Adjective, semanticTags: new[] { "STEADY" }, aliases: new[] { "SILENT" }, attackMultiplier: 0.92f, defenseMultiplier: 1.12f, speedMultiplier: 1.05f),
            BuildModifier("SOFT", ModifierGrammarRole.Adjective, semanticTags: new[] { "CAREFUL" }, aliases: new[] { "PLUSH" }, attackMultiplier: 0.85f, defenseMultiplier: 1.08f, speedMultiplier: 1.05f),
            BuildModifier("HARD", ModifierGrammarRole.Adjective, semanticTags: new[] { "DEFENSE" }, aliases: new[] { "FIRM", "STIFF" }, attackMultiplier: 1.08f, defenseMultiplier: 1.18f, speedMultiplier: 0.9f),
            BuildModifier("CLEAN", ModifierGrammarRole.Adjective, semanticTags: new[] { "PRECISION" }, aliases: new[] { "NEAT", "TIDY" }, defenseMultiplier: 1.05f, accuracyMultiplier: 1.08f),
            BuildModifier("DIRTY", ModifierGrammarRole.Adjective, semanticTags: new[] { "RISK" }, aliases: new[] { "MESSY", "MUDDY" }, attackMultiplier: 1.08f, defenseMultiplier: 0.92f, accuracyMultiplier: 0.92f),
            BuildModifier("HEAVY", ModifierGrammarRole.Adjective, semanticTags: new[] { "POWER", "HP" }, attackMultiplier: 1.18f, defenseMultiplier: 1.12f, speedMultiplier: 0.78f),
            BuildModifier("LIGHT", ModifierGrammarRole.Adjective, semanticTags: new[] { "SPEED" }, aliases: new[] { "LIGHTWEIGHT" }, maxHpMultiplier: 0.92f, defenseMultiplier: 0.92f, speedMultiplier: 1.25f, evasionMultiplier: 1.08f),
            BuildModifier("TALL", ModifierGrammarRole.Adjective, semanticTags: new[] { "REACH" }, maxHpMultiplier: 1.08f, attackMultiplier: 1.08f, speedMultiplier: 0.94f),
            BuildModifier("SHORT", ModifierGrammarRole.Adjective, semanticTags: new[] { "EVADE" }, aliases: new[] { "LOW" }, maxHpMultiplier: 0.94f, defenseMultiplier: 1.02f, speedMultiplier: 1.14f, evasionMultiplier: 1.08f),
            BuildModifier("WIDE", ModifierGrammarRole.Adjective, semanticTags: new[] { "HP" }, aliases: new[] { "BROAD" }, maxHpMultiplier: 1.12f, defenseMultiplier: 1.08f, speedMultiplier: 0.92f),
            BuildModifier("NARROW", ModifierGrammarRole.Adjective, semanticTags: new[] { "PRECISION" }, aliases: new[] { "SLIM" }, defenseMultiplier: 0.96f, speedMultiplier: 1.12f, accuracyMultiplier: 1.08f),
            BuildModifier("FULL", ModifierGrammarRole.Adjective, semanticTags: new[] { "HP" }, aliases: new[] { "FILLED" }, maxHpMultiplier: 1.14f, defenseMultiplier: 1.08f, speedMultiplier: 0.9f),
            BuildModifier("EMPTY", ModifierGrammarRole.Adjective, semanticTags: new[] { "SPEED" }, maxHpMultiplier: 0.88f, defenseMultiplier: 0.9f, speedMultiplier: 1.2f),
            BuildModifier("HAPPY", ModifierGrammarRole.Adjective, semanticTags: new[] { "SPEED" }, aliases: new[] { "GLAD", "CHEERFUL" }, attackMultiplier: 1.04f, defenseMultiplier: 1.02f, speedMultiplier: 1.12f),
            BuildModifier("SAD", ModifierGrammarRole.Adjective, semanticTags: new[] { "HEAVY" }, aliases: new[] { "UPSET", "UNHAPPY" }, attackMultiplier: 0.94f, defenseMultiplier: 1.08f, speedMultiplier: 0.94f),
            BuildModifier("KIND", ModifierGrammarRole.Adjective, semanticTags: new[] { "CAREFUL" }, aliases: new[] { "NICE", "FRIENDLY" }, attackMultiplier: 0.9f, defenseMultiplier: 1.14f, speedMultiplier: 1.02f),
            BuildModifier("ANGRY", ModifierGrammarRole.Adjective, semanticTags: new[] { "POWER" }, aliases: new[] { "MAD" }, attackMultiplier: 1.18f, defenseMultiplier: 0.94f, speedMultiplier: 1.02f),
            BuildModifier("SHY", ModifierGrammarRole.Adjective, semanticTags: new[] { "STEALTH" }, aliases: new[] { "TIMID" }, attackMultiplier: 0.9f, defenseMultiplier: 1.08f, evasionMultiplier: 1.12f),
            BuildModifier("PROUD", ModifierGrammarRole.Adjective, semanticTags: new[] { "POWER" }, attackMultiplier: 1.1f, defenseMultiplier: 1.04f, speedMultiplier: 0.98f),
            BuildModifier("SMART", ModifierGrammarRole.Adjective, semanticTags: new[] { "PRECISION" }, aliases: new[] { "CLEVER", "WISE" }, attackMultiplier: 1.02f, defenseMultiplier: 1.04f, accuracyMultiplier: 1.12f),
            BuildModifier("CURIOUS", ModifierGrammarRole.Adjective, semanticTags: new[] { "SPEED" }, aliases: new[] { "EAGER" }, defenseMultiplier: 0.96f, speedMultiplier: 1.14f, accuracyMultiplier: 1.05f),
            BuildModifier("SAFE", ModifierGrammarRole.Adjective, semanticTags: new[] { "DEFENSE" }, aliases: new[] { "SECURE" }, attackMultiplier: 0.94f, defenseMultiplier: 1.18f, speedMultiplier: 0.96f),
            BuildModifier("WET", ModifierGrammarRole.Adjective, semanticTags: new[] { "RISK" }, attackMultiplier: 1.04f, defenseMultiplier: 0.94f, speedMultiplier: 0.98f),
            BuildModifier("DRY", ModifierGrammarRole.Adjective, semanticTags: new[] { "STABLE" }, defenseMultiplier: 1.08f, accuracyMultiplier: 1.05f),
            BuildModifier("YOUNG", ModifierGrammarRole.Adjective, semanticTags: new[] { "SPEED" }, attackMultiplier: 0.98f, speedMultiplier: 1.16f, evasionMultiplier: 1.06f),
            BuildModifier("OLD", ModifierGrammarRole.Adjective, semanticTags: new[] { "DEFENSE" }, attackMultiplier: 1.04f, defenseMultiplier: 1.14f, speedMultiplier: 0.9f),
            BuildModifier("FAST", ModifierGrammarRole.Adverb, semanticTags: new[] { "COSTLY", "SPEED" }, aliases: new[] { "QUICKLY", "RAPIDLY", "SWIFTLY", "BRISKLY", "SPEEDILY" }, allowedVerbTags: new[] { "MOVEMENT", "OFFENSE" }, speedMultiplier: 1.5f, powerMultiplier: 1.1f, accuracyMultiplier: 0.92f, evasionMultiplier: 1.25f, ppCostMultiplier: 1.35f),
            BuildModifier("SLOWLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "CHEAP", "CAREFUL" }, aliases: new[] { "SLOW" }, allowedVerbTags: new[] { "MOVEMENT", "DEFENSE", "UTILITY" }, defenseMultiplier: 1.2f, speedMultiplier: 0.65f, powerMultiplier: 0.85f, accuracyMultiplier: 1.15f, evasionMultiplier: 0.8f, ppCostMultiplier: 0.65f),
            BuildModifier("GENTLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "CHEAP", "CAREFUL" }, aliases: new[] { "LIGHTLY", "SOFTLY" }, allowedVerbTags: new[] { "DEFENSE", "UTILITY", "OFFENSE" }, powerMultiplier: 0.72f, defenseMultiplier: 1.15f, accuracyMultiplier: 1.08f, ppCostMultiplier: 0.7f),
            BuildModifier("HEAVILY", ModifierGrammarRole.Adverb, semanticTags: new[] { "COSTLY", "POWER" }, aliases: new[] { "BOLDLY", "FIERCELY", "FIRMLY" }, allowedVerbTags: new[] { "OFFENSE", "UTILITY" }, powerMultiplier: 1.35f, accuracyMultiplier: 0.82f, speedMultiplier: 0.85f, ppCostMultiplier: 1.55f),
            BuildModifier("CAREFULLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "PRECISE" }, aliases: new[] { "CALMLY", "NEATLY", "PRECISELY" }, allowedVerbTags: new[] { "DEFENSE", "UTILITY", "MOVEMENT", "OFFENSE" }, powerMultiplier: 0.92f, accuracyMultiplier: 1.25f, defenseMultiplier: 1.1f, ppCostMultiplier: 0.95f),
            BuildModifier("QUIETLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "CHEAP", "DEFENSE" }, aliases: new[] { "SOFT" }, allowedVerbTags: new[] { "DEFENSE", "UTILITY", "OFFENSE" }, powerMultiplier: 0.72f, defenseMultiplier: 1.15f, accuracyMultiplier: 1.08f, ppCostMultiplier: 0.7f),
            BuildModifier("SHARPLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "POWER", "PRECISION" }, aliases: new[] { "CLEANLY" }, allowedVerbTags: new[] { "OFFENSE" }, powerMultiplier: 1.18f, accuracyMultiplier: 1.04f, speedMultiplier: 0.96f, ppCostMultiplier: 1.18f),
            BuildModifier("SMOOTHLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "SPEED", "PRECISION" }, aliases: new[] { "EVENLY" }, allowedVerbTags: new[] { "MOVEMENT", "UTILITY" }, speedMultiplier: 1.2f, accuracyMultiplier: 1.08f, ppCostMultiplier: 1f),
            BuildModifier("CLEARLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "PRECISION" }, aliases: new[] { "PLAINLY" }, allowedVerbTags: new[] { "UTILITY", "DEFENSE", "OFFENSE" }, accuracyMultiplier: 1.2f, defenseMultiplier: 1.05f, ppCostMultiplier: 0.95f),
            BuildModifier("PATIENTLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "CHEAP", "DEFENSE" }, aliases: new[] { "STEADILY" }, allowedVerbTags: new[] { "UTILITY", "DEFENSE", "MOVEMENT" }, speedMultiplier: 0.82f, defenseMultiplier: 1.18f, accuracyMultiplier: 1.08f, ppCostMultiplier: 0.82f),
            BuildModifier("SAFELY", ModifierGrammarRole.Adverb, semanticTags: new[] { "DEFENSE" }, allowedVerbTags: new[] { "DEFENSE", "MOVEMENT", "UTILITY" }, speedMultiplier: 0.9f, defenseMultiplier: 1.16f, accuracyMultiplier: 1.06f, ppCostMultiplier: 0.9f),
            BuildModifier("EAGERLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "SPEED" }, allowedVerbTags: new[] { "UTILITY", "MOVEMENT", "OFFENSE" }, speedMultiplier: 1.16f, powerMultiplier: 1.05f, accuracyMultiplier: 0.96f, ppCostMultiplier: 1.08f),
            BuildModifier("LOUDLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "POWER" }, aliases: new[] { "NOISILY" }, allowedVerbTags: new[] { "OFFENSE", "UTILITY" }, powerMultiplier: 1.16f, accuracyMultiplier: 0.94f, ppCostMultiplier: 1.12f),
            BuildModifier("CLOSELY", ModifierGrammarRole.Adverb, semanticTags: new[] { "PRECISION" }, allowedVerbTags: new[] { "UTILITY", "DEFENSE" }, accuracyMultiplier: 1.14f, defenseMultiplier: 1.06f, ppCostMultiplier: 0.96f),
            BuildModifier("OPENLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "RISK" }, allowedVerbTags: new[] { "OFFENSE", "UTILITY" }, powerMultiplier: 1.08f, defenseMultiplier: 0.92f, ppCostMultiplier: 1.05f),
            BuildModifier("WARMLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "STEADY" }, allowedVerbTags: new[] { "UTILITY", "DEFENSE" }, defenseMultiplier: 1.08f, accuracyMultiplier: 1.04f, ppCostMultiplier: 0.96f),
            BuildModifier("COOLLY", ModifierGrammarRole.Adverb, semanticTags: new[] { "PRECISE" }, allowedVerbTags: new[] { "UTILITY", "DEFENSE", "OFFENSE" }, accuracyMultiplier: 1.12f, powerMultiplier: 0.98f, ppCostMultiplier: 0.96f),
            BuildModifier("HAPPILY", ModifierGrammarRole.Adverb, semanticTags: new[] { "SPEED" }, allowedVerbTags: new[] { "UTILITY", "MOVEMENT" }, speedMultiplier: 1.1f, defenseMultiplier: 1.02f, ppCostMultiplier: 1f),
            BuildModifier("BRAVELY", ModifierGrammarRole.Adverb, semanticTags: new[] { "POWER" }, allowedVerbTags: new[] { "OFFENSE", "DEFENSE" }, powerMultiplier: 1.14f, defenseMultiplier: 1.04f, ppCostMultiplier: 1.08f),
        };
    }

    static List<string> TagBasedAdverbs(string family)
    {
        switch (CreaturePhraseUtility.NormalizeToken(family))
        {
            case "OFFENSE":
                return new List<string> { "GENTLY", "HEAVILY", "SHARPLY", "CAREFULLY", "FAST", "LOUDLY", "BRAVELY", "OPENLY", "COOLLY" };
            case "DEFENSE":
                return new List<string> { "SLOWLY", "CAREFULLY", "QUIETLY", "GENTLY", "SMOOTHLY", "PATIENTLY", "SAFELY", "WARMLY", "BRAVELY" };
            case "MOVEMENT":
                return new List<string> { "FAST", "SLOWLY", "CAREFULLY", "SMOOTHLY", "HEAVILY", "EAGERLY", "SAFELY", "HAPPILY" };
            case "UTILITY":
                return new List<string> { "GENTLY", "SLOWLY", "CAREFULLY", "QUIETLY", "SMOOTHLY", "CLEARLY", "CLOSELY", "PATIENTLY", "EAGERLY", "COOLLY", "WARMLY" };
            default:
                return new List<string>();
        }
    }

    static List<string> ToList(IEnumerable<string> values)
    {
        return values != null ? new List<string>(values) : new List<string>();
    }

    static void AddTag(List<string> tags, string tag)
    {
        string normalized = CreaturePhraseUtility.NormalizeToken(tag);
        if (string.IsNullOrEmpty(normalized) || tags.Contains(normalized))
            return;
        tags.Add(normalized);
    }
}
