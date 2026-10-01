import crypto from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { DatabaseSync } from "node:sqlite";

const apiBase = (process.env.IMAGE_EDIT_BASE || "http://127.0.0.1:3100").replace(/\/$/, "");
const accountName = process.env.IMAGE_EDIT_USERNAME?.trim();
const model = "gpt-image-2";
const sourceImage = process.env.IMAGE_EDIT_SOURCE_IMAGE?.trim();
const expectedSourceSha256 = "a32479e8efbaf788cf6d0b57355a3cdad100a53ff2c5954df07e472e5742964b";
const databasePath = process.env.IMAGE_EDIT_DATABASE_PATH?.trim();
const outputRoot = fileURLToPath(new URL("../assets/sprites/source-api/", import.meta.url));

const sharedPrompt = `
Use the uploaded character image as the strict identity and costume reference. Create a professional 2D game animation sprite sheet for exactly the same chibi girl: enormous rounded head, pale warm face, long fluffy gray-blue hair, red-and-white shrine-maiden-like outfit with the same large red bow, short dark skirt, and two tiny rounded feet. Extend the reference into a complete full body while preserving its design language.

OUTPUT CONTRACT (must follow exactly):
- one square 2048 x 2048 sprite sheet, exactly 4 columns by 4 rows, exactly 16 equal cells
- animation order is left-to-right, then top-to-bottom
- exactly one complete character in every cell
- same character design, colors, costume, hair length, outline weight, body proportions and camera scale in all 16 cells
- every frame is full body; both tiny feet remain clearly visible, including standing poses
- both eyes preserve the reference's solid filled pink-to-pale-blue gradient with no separate pupils; never hollow, blank, white-only or transparent
- consistent ground/baseline and generous equal padding; nothing touches or crosses a cell boundary
- no captions, letters, numbers, labels, guides, borders, grid lines, props, particles, shadows, scenery or extra characters
- every cell background is perfectly flat, uniform chroma magenta #FF00FF, with no texture, gradient, antialias haze, shadow or glow in the background
- crisp clean anime line art, compact desktop-pet silhouette, very soft mochi/squishy body, cute and appetitive personality
- poses and facial acting are exaggerated and readable at 128 pixels, but identity and costume do not drift
- this is one coherent continuous 16-frame animation, not a collection of unrelated illustrations; looping actions must connect frame 16 smoothly back to frame 1
- in every cell the complete silhouette occupies at most 68 percent of cell height and 74 percent of cell width, with at least 14 percent empty magenta above the highest hair point and generous side clearance; never crop the head, hair, sleeves or feet
`;

const prompts = {
  idle: `${sharedPrompt}\nANIMATION: idle breathing loop. Start and end in the same standing pose. Show a smooth squash-and-stretch breathing cycle: body gently compresses, cheeks puff, hair and bow lag with soft secondary motion, then rebound with a subtle Q-bouncy overshoot. Keep the pupil-less half-lidded eyes calm and stable with one quick blink; do not track a cursor. Keep both feet planted and visible in all frames. Use small incremental pose changes so adjacent frames animate smoothly.`,
  run: `${sharedPrompt}\nANIMATION: fast hungry scamper loop toward the right. Exaggerated anticipation, elastic launch, alternating tiny feet, strong squash on contact, long stretch in the air, hair and bow trailing then overshooting. The face is gleefully hungry and determined; solid eyes look toward the running direction. Keep the entire character and both feet inside every cell. Frame 16 must transition smoothly back to frame 1.`,
  chomp: `${sharedPrompt}\nANIMATION: an exceptionally eye-catching, overacted and irresistibly cute 16-frame LEFT-FACING suction performance synchronized with a real deleted-icon ghost. Personality must dominate every pose: soft, adorable, shamelessly greedy, hungry, bouncy, squishy and mochi-like. The invisible icon is on the LEFT; the character stays on its RIGHT and faces LEFT. This action needs a CLEAR, VERY LARGE OPEN MOUTH like the iconic inhale pose of a cute round video-game creature. The large mouth must be instantly readable even at 128 pixels.\n\nHEAD AND MOUTH DESIGN: keep the normal rounded outer silhouette of her head and face; do not project, lengthen or reshape the face into a muzzle. During maximum suction, open a huge vertical rounded bean-shaped mouth INSIDE the left side of the round face, about 52 to 60 percent of head height and 36 to 44 percent of head width. The mouth opening is recessed within the face contour, never protruding forward. Give it a thick soft peach-pink rim, a clearly visible warm coral/cherry-red inner mouth, and a small lighter pink tongue surface near the bottom. The interior is colorful and softly shaded, never black. Keep the pupil-less pink-to-pale-blue gradient eyes visible above and behind the mouth; they may squash into delighted crescents under effort but must not become hollow or gain pupils. The result must feel like an adorable hungry mochi opening wide enough to swallow an app icon, not a tiny O-shaped mouth.\n\nUse strong classical animation principles: clear anticipation, bold whole-body squash-and-stretch, overshoot, follow-through and secondary hair/bow/sleeve motion. Frames 1-2: she notices invisible food, lights up greedily, lifts her little hands and compresses into a short round mochi, both feet visible. Frames 3-4: dramatic anticipation—both feet plant and skid, the intact round head and torso lean far backward to the RIGHT as one unit, sleeves spread, hair and bow lag in a big soft arc. Frames 5-6: the mouth visibly opens from medium to large, with the warm red interior already clear; her torso squashes and then stretches. Frames 7-10: strongest suction and signature silhouette—the mouth is fully open at 52 to 60 percent of head height, a broad vertical oval/bean opening contained entirely inside her round face. She braces at a bold 30-to-40-degree backward lean, feet comically gripping/sliding, while hair, bow and empty sleeves stream far to the RIGHT. Alternate one pancake squash and one elastic recoil so the body feels Q-bouncy, but keep the mouth location stable enough for the real icon overlay to enter. Frame 11: invisible food reaches deep into the open mouth; the whole character snaps forward into a compact soft mochi-ball rebound while the mouth starts closing. Frame 12: lips seal into a tiny pout and both cheeks instantly inflate into large round buns. Frame 13: very puffed cheeks plus a low full-body squash. Frame 14: exaggerated left-right chewing wobble with hair, bow and sleeves following one beat late. Frame 15: clear swallow—the whole body stretches upward like soft taffy, then dress and cheeks rebound. Frame 16: settle into a smug sleepy satisfied smile, one hand near the fully clothed belly, both feet planted. Adjacent poses must change boldly yet flow as one readable action. Draw absolutely no food, file, app icon, prop, suction particles or motion marks because the program overlays the real icon. STRICTLY FORBIDDEN: tiny dot mouth during frames 7-10, protruding lips, stretched face, muzzle, snout, proboscis, trunk, hose, cone, tube, trumpet, beak, external mouth tunnel, black void, teeth, fangs, saliva, horror expression, missing eyes, new pupils, or holding/gripping anything. Keep the complete head, open mouth, hair, costume, hands and both feet inside every cell with generous magenta clearance.`,
  satisfied: `${sharedPrompt}\nANIMATION: satisfied post-meal loop. Start neutral, puff both cheeks, close the eyes in delight, press both hands against the fully clothed belly, give a tiny smug lip-lick, then perform a large full-body squash, stretch and soft jelly wobble that settles back to the original standing pose. Cute, greedy, warm and comically overacted. The complete costume and large chest bow remain present in every frame; never expose bare belly skin. Both tiny feet remain visible in every frame. Absolutely no steam, drool, sweat, liquid, sparkles, motion marks, punctuation-like symbols or detached decorative elements. CRITICAL FRAMING RULE: in every one of the 16 cells, the complete silhouette must occupy no more than 60 percent of the cell height and 70 percent of the cell width. Keep at least 15 percent empty magenta clearance above the highest hair point and on every side. The bottom-row characters must be fully contained below the row boundary; never crop, flatten, truncate or hide the top of the head or hair.`,
  walk: `${sharedPrompt}
ANIMATION: a gentle, cute 16-frame walking loop for moving along the top edge of a Windows app window. She faces RIGHT; the program may mirror the finished frames. This is a slower everyday walk, clearly different from the existing fast hungry run. Keep one continuous camera, scale and foot baseline. The pupil-less pink-to-pale-blue gradient eyes stay calm and stable; no cursor tracking.

FRAME FLOW: frames 1-2 soft anticipation with a tiny forward lean; frames 3-5 right foot takes one short step while the body compresses then rises; frames 6-8 weight settles with a plump mochi wobble and delayed hair/bow follow-through; frames 9-11 left foot takes the matching short step; frames 12-14 settle with the opposite hair/bow overshoot; frames 15-16 return smoothly to frame 1. Both tiny feet must remain clearly drawn and alternate contact without vanishing, merging or changing shape. Exaggerate the soft up-down bounce, cheek jiggle and sleeve lag, but keep adjacent frames incremental and the head center stable. Cute, relaxed and slightly hungry, never frantic. No running leap, no floating, no props, no particles, no motion lines, no text.`,
  fall: `${sharedPrompt}
ANIMATION: a seamless 16-frame AIRBORNE FALLING loop, used while the program physically moves the desktop pet downward under gravity. There is no ground or platform drawn. Keep the character centered at one camera scale; do not animate vertical screen translation inside the cells. Both tiny feet remain fully visible dangling beneath the outfit in every frame.

FRAME FLOW: frames 1-3 surprised weightless lift with the round body slightly stretched upward; frames 4-6 hair, bow and sleeves stream upward as gravity takes hold; frames 7-9 strongest soft downward stretch, cheeks and skirt lag upward, feet point downward; frames 10-12 body rebounds into a rounder mochi shape while still airborne; frames 13-16 hair and bow perform a small delayed flutter that returns seamlessly to frame 1. Facial acting is cute and comically startled—small warm open mouth, squeezed brows, pupil-less gradient eyes—never terrified or hollow. The silhouette may squash/stretch by at most 12 percent between extremes and only 4 percent between adjacent frames. No ground contact, landing pose, shadow, clouds, speed lines, punctuation, tears, props or extra objects.`,
  land: `${sharedPrompt}
ANIMATION: one non-looping 16-frame Q-BOUNCY LANDING reaction immediately after a fall. The invisible platform is beneath her but must not be drawn. Lock the same bottom contact line in every cell and keep both tiny feet visible, including the deepest squash. Start airborne just above contact and finish in the normal stable standing pose.

EXACT FLOW: frame 1 feet about to touch, body slightly vertically stretched; frame 2 first toe contact; frame 3 both feet contact and knees/body begin compressing; frame 4 stronger compression; frame 5 maximum adorable pancake-like mochi squash, wide but never flattened enough to hide face, bow, costume or feet; frame 6 hold the squash for one beat; frames 7-8 elastic upward rebound with hair and sleeves still low; frame 9 small overshoot stretch; frames 10-11 settle downward with cheeks wobbling; frames 12-13 second much smaller bounce; frames 14-15 nearly stable; frame 16 exact neutral standing settle. Adjacent frames must be true in-betweens with no teleporting or scale jump. Expression changes from braced surprise to relieved sleepy smile. No impact star, dust, lines, platform, cracks, text, props or detached particles.`,
  curious: `${sharedPrompt}
ANIMATION: a seamless 16-frame CUTE CURIOUS EXPLORATION loop performed while standing on a Windows window edge. She investigates the surrounding digital world like it is physically real, but no window, cursor or object is drawn. Both feet stay planted, separate and visible on the same baseline. The pupil-less pink-to-pale-blue gradient eyes remain stable and do not track a mouse.

FRAME FLOW: frames 1-3 ears-not-present/attention moment expressed only through a big head tilt and lifted shoulders; frames 4-6 lean forward from the ankles with a tiny sniffing pout and hands tucked near the chest bow; frames 7-9 deepest curious lean, cheeks gently squished, long hair and sleeves lag behind; frames 10-12 one exaggerated soft side-to-side head tilt with a delayed bow bounce; frames 13-16 recoil into a round mochi wobble and return seamlessly to neutral. Make the curiosity instantly readable at 128 pixels through bold head tilt, forward lean and soft cheek acting. No magnifying glass, question mark, cursor, pupils, animal ears, props, particles, text or motion marks.`,
  sleep: `${sharedPrompt}
ANIMATION: a seamless 16-frame SOFT STANDING-DOZE loop suitable for sleeping safely on a narrow window ledge. She gradually squishes into a low round mochi resting pose while the two tiny feet continue peeking out clearly and the complete outfit remains intact. Eyes close into gentle filled gradient-tinted crescents; never blank white sockets and never add pupils. No pillow or blanket.

FRAME FLOW: frames 1-3 eyelids become heavy and shoulders droop; frames 4-6 head nods forward and body slowly compresses; frames 7-10 lowest round sleepy pose with both feet visible, chest bow softly resting against the clothed body, subtle breathing expansion; frames 11-13 a tiny sleepy startle causes one gentle vertical rebound; frames 14-16 settle back into the same low breathing pose and connect smoothly to frame 1. Secondary hair, bow and sleeves move one beat late like soft fabric. Mood is safe, warm, cuddly and very sleepy. No lying prone, no exposed belly, no drool, no bubbles, no Z letters, no punctuation, no particles, no props, no floor or shadow.`,
  hungry: `${sharedPrompt}
ANIMATION: a seamless 16-frame ADORABLY HUNGRY begging loop. She is greedy and food-motivated but never sad or distressed. Keep both feet planted and visible. Draw no food or file because actual deleted icons are supplied by the program. The pupil-less pink-to-pale-blue gradient eyes remain filled and stable with no cursor tracking.

FRAME FLOW: frames 1-3 she notices her empty tummy, hands move toward the fully clothed belly and cheeks deflate into a tiny pout; frames 4-6 body compresses into a round pleading mochi and the big bow squishes softly; frames 7-9 strongest comic hunger pose—one hand hugs the clothed belly, the other reaches forward empty, head tilts, mouth becomes a cute warm bean-shaped pout; frames 10-12 she gives one exaggerated hopeful Q-bouncy rise with hair and sleeves following late; frames 13-16 settles into a smug determined little hungry stance that loops back to frame 1. Make the gesture bold and charming at 128 pixels, not ordinary. No exposed skin, ribs, sickness, tears, drool, tongue, food, bowl, icon, props, text, punctuation, particles or motion marks.`,
  lick: `${sharedPrompt}
ANIMATION: one non-looping 16-frame LEFT-FACING cute icon-lick action. An actual Windows desktop icon will remain visible on the LEFT as a separate program layer, so draw absolutely no icon, file, food, prop, panel or target. Keep the character on the RIGHT side of every cell, facing LEFT. Her tongue must touch the same invisible contact point on the LEFT during the middle frames so the real icon lines up convincingly.

PERSONALITY AND DESIGN: she is shamelessly greedy, adorably hungry, Q-bouncy, squishy and soft like mochi. The lick must be funny and eye-catching but never creepy. Preserve the exact round face, gray-blue hair, red-and-white outfit, large bow, solid pupil-less gradient eyes and two tiny feet. The tongue is a short, broad, rounded strawberry-pink mochi tongue with a blunt heart-soft tip, never thin, pointed, forked or snake-like. Use a small warm open mouth; do not stretch the face into a muzzle. No saliva, drool, wet strings, teeth, black mouth void or fetish-like expression.

COMPOSITION LOCK: keep the face center and both-foot baseline fixed across all cells. Place the character body around 58 percent of each cell width. At maximum extension, the rounded tongue tip reaches about 34 percent of cell width and 48 percent of cell height, leaving generous magenta clearance and never touching a cell boundary. Frames 6 through 10 must keep that tongue contact point nearly stationary. Keep the entire head, hair, sleeves, skirt, tongue and both feet fully inside every cell.

FRAME FLOW: frames 1-2 she notices the invisible icon and her eyes brighten greedily; frame 3 she compresses into a low round anticipation squash with both feet planted; frame 4 leans LEFT from the ankles while hair and bow lag RIGHT; frame 5 opens a small warm mouth and the rounded tongue just peeks out; frame 6 tongue extends halfway toward the fixed contact point; frame 7 tongue reaches the invisible icon; frame 8 maximum cute lick—the broad tongue tip softly flattens as if touching a cool glassy icon, cheeks squash and the body stretches slightly LEFT; frame 9 hold nearly the same contact pose with only a tiny Q-bouncy cheek and sleeve wobble; frame 10 begin retracting while the contact point changes by no more than 3 percent; frame 11 tongue retracts halfway; frame 12 tongue disappears and lips close into a pleased tiny pout; frame 13 cheeks puff with a smug taste-test expression; frame 14 full-body jelly squash; frame 15 small elastic rebound; frame 16 settles into a delighted hungry standing pose with both feet visible. Adjacent frames must be smooth true in-betweens, not unrelated poses.

STRICTLY FORBIDDEN: drawing the icon or any object, giant tongue, long dangling tongue, sharp or forked tongue, tongue coming from outside the mouth, muzzle, snout, protruding face, drool, saliva, liquid, licking the viewer, horror, suggestive acting, new pupils, mouse tracking, missing feet, cropped head, motion lines, sparkles, punctuation, text or detached particles.`,
  toss: `${sharedPrompt}
ANIMATION: one coherent 16-frame AIRBORNE UPWARD-TOSS reaction for a desktop pet thrown into the air by a Windows app window moving rapidly upward, like a cute miniature drop-tower ride. There is no platform, window, ground or shadow drawn. The program physically moves her along a ballistic arc, so keep one fixed camera and do not animate screen translation inside the cells.

PHYSICS AND ACTING: upward inertia is instantly readable. Her two tiny feet lift and dangle below the complete outfit in every frame. During ascent, the soft round body stretches slightly upward while long gray-blue hair, the large red bow, sleeves and skirt lag DOWNWARD from inertia; near the apex, the body becomes a plump weightless mochi ball and all secondary parts float upward one beat late. Her expression is exaggerated, adorable and surprised: pupil-less filled pink-to-pale-blue gradient eyes squeeze wider/rounder without gaining pupils, cheeks wobble, and a small warm bean-shaped mouth opens. Never look terrified, hollow or injured.

EXACT CONTINUOUS FLOW: frames 1-2 sudden launch anticipation, both feet just leaving the invisible support, body compressed only 5 percent; frames 3-5 strong upward stretch with feet dangling and hair/bow/sleeves clearly trailing downward; frames 6-8 continued ascent with a soft cheek wobble and delayed costume follow-through; frames 9-11 near-apex weightlessness, body rounds into a buoyant mochi shape while hair and sleeves begin floating upward; frames 12-14 tiny apex overshoot, feet kick once in a cute helpless bicycle motion but both remain visible; frames 15-16 start settling toward the existing falling animation with the body gently lengthening downward. Adjacent frames must be true small in-betweens: same head scale, same face center, same full-body framing, no teleporting, no sudden prone pose, and no more than 5 percent silhouette-size change per adjacent frame.

Keep at least 14 percent empty magenta above the highest hair point even at maximum upward stretch. Keep the complete head, hair ends, bow, sleeves, skirt and both feet inside every cell. Exaggeration comes from squash-and-stretch, dangling feet, cheek wobble and delayed hair/costume inertia. STRICTLY FORBIDDEN: platform, window, ground, impact pose, lying down, cropped head, missing feet, new pupils, cursor tracking, tears, sweat, vomit, drool, speed lines, arrows, punctuation, text, particles, props or extra characters.`,
};

prompts.toss += `
CUTE PROPORTION OVERRIDE (highest priority): preserve her enormous round head and tiny compact torso as one connected chibi mochi unit in all 16 frames. The chin-to-bow distance, neck length and face-to-torso spacing must remain nearly identical to the original standing reference. Never pull the head away from the shoulders. Never expose or invent a long neck, long pale torso, stretched skin column, noodle body, giraffe neck or taffy-like connection. Total character height may vary only from 92 to 108 percent of neutral height; adjacent frames may vary by at most 3 percent. Head width and height remain constant within 3 percent. Both tiny feet remain directly beneath the skirt and clearly visible.

Make the upward force cute and readable through the WHOLE compact body shifting as one unit, a 6-percent standing squash followed by an 8-percent full-body stretch, feet dangling/kicking, cheeks wobbling, and especially strong delayed hair, bow, sleeve and skirt motion. The body itself stays short, round and plush. Frames 1-3 are compact lift-off; frames 4-7 are the same compact body airborne with hair and bow trailing downward; frames 8-11 are a round weightless apex pose with hands and feet floating slightly outward; frames 12-14 are a tiny sideways mochi wobble; frames 15-16 reconnect to a compact upright falling pose. No frame may resemble a long-neck creature. No detached white launch lines, speed marks, emphasis symbols or decorative fragments.`;

// These life-behavior sheets are always regenerated from the verified original
// character image. They are never edits of existing sleep or movement frames.
prompts.sleep = `${sharedPrompt}
ANIMATION: a non-looping lie-down transition followed by a seamless side-lying sleep-breathing loop, designed for a tiny creature sleeping directly on an invisible Windows window ledge. The window, floor, pillow and blanket are NOT drawn. Preserve the exact character, gray-blue hair, red-and-white outfit, large red bow and filled pupil-less gradient eyes.

COMPOSITION AND CONTACT: keep one fixed camera and one fixed invisible floor baseline. The complete character must remain inside every cell with generous magenta space above and on both sides. During the lying pose, arrange the long hair and outfit into one compact connected horizontal mochi silhouette no wider than 72 percent of the cell. Both tiny feet are clearly visible in all standing/crouching transition frames; in the final lying frames at least one foot remains visibly peeking from beneath the skirt/hair. Never crop the head, hair, bow, hands or feet. No detached hair clumps or accessories.

EXACT FLOW: frame 1 normal upright sleepy stance with both feet visible; frame 2 eyelids droop and shoulders sink; frame 3 knees and whole body compress into a short round crouch; frame 4 she lowers one hand and tips gently to her RIGHT as one compact unit; frame 5 hip and hair make first soft floor contact; frame 6 rolls onto her side with head supported by the fluffy hair; frame 7 settles the skirt, bow and visible tiny foot; frame 8 reaches the final cozy side-lying pose. Frames 9-16 remain in that SAME side-lying pose and form a seamless breathing loop: belly/chest and cheeks expand only 3 percent, long hair and bow rise one beat late, then settle. Frames 9 and 16 are near-duplicates so the breathing loop is smooth. Eyes close into soft filled pink-blue-tinted crescents, mouth is a tiny warm sleepy curve.

The movement must read as soft, safe, cuddly and Q-bouncy—not collapsed, injured or unconscious. Adjacent frames are true in-betweens with no teleporting, camera recentering or sudden scale change. STRICTLY FORBIDDEN: standing sleep after frame 8, face-down pose, exposed underwear or belly, bare skin added to the outfit, missing head, long neck, new pupils, hollow eyes, tears, drool, sleep bubble, Z letters, punctuation, pillow, blanket, bed, floor, shadow, props, particles, motion lines or extra characters.`;

prompts.climb = `${sharedPrompt}
ANIMATION: a seamless 16-frame energetic VERTICAL CLIMBING loop. The program moves the desktop pet upward beside a real Windows app window, so draw NO window, ledge, wall, rope, ladder or scenery. She faces RIGHT toward an invisible vertical edge; the program may mirror the final frames for a left-side climb. Keep one fixed camera and do not animate overall screen translation inside the cells.

POSE AND PERSONALITY: preserve her huge round head and tiny compact mochi torso as one connected unit, with the original gray-blue hair, red-and-white outfit, large bow and pupil-less filled gradient eyes. Her expression is adorably determined and a little greedy: puffed cheeks, tiny warm effort mouth, no fear. Both tiny feet stay clearly visible below the skirt in every frame, alternately paddling or pressing against the invisible side. Hands and sleeves alternate upward reaches but never hold a drawn object. The body stays short, round and plush; never stretch the neck or expose a pale body column.

LOOP FLOW: frames 1-2 compact anticipation with both feet tucked and the upper hand reaching; frames 3-5 whole-body elastic upward pull, opposite foot presses and hair/bow lag downward; frames 6-8 soft high-point squash with cheeks wobbling; frames 9-10 hands switch smoothly; frames 11-13 second upward pull with the other foot pressing; frames 14-16 settle into the same compact pose as frame 1, ready to repeat. Use strong but incremental squash-and-stretch, alternating limbs, delayed sleeves/hair and one tiny determined head bob. The silhouette center and scale remain stable because physical movement is handled by code.

STRICTLY FORBIDDEN: drawn window or ledge, ladder, rope, wall, handhold, platform, floor, shadow, prop, motion lines, dust, sweat, tears, punctuation, text, detached fragments, extra characters, long neck, noodle torso, missing feet, cropped head, hollow eyes, new pupils or mouse tracking.`;

prompts.roll = `${sharedPrompt}
ANIMATION: one non-looping, full 16-frame HUNGRY TANTRUM ROLL performed in place on an invisible Windows ledge. She starts upright, curls into a compact soft mochi ball, completes one readable sideways barrel-roll/somersault on the floor, then springs back upright. Draw NO floor, window, food, icon or prop. The result must be funny, irresistibly cute, shamelessly hungry, Q-bouncy and soft—not hurt, dizzy, creepy or distressed.

IDENTITY AND ROTATION LOCK: preserve the exact enormous round head, tiny body, long gray-blue hair, red-and-white outfit, large bow and filled pupil-less gradient eyes throughout. Rotate the WHOLE compact clothed character as one connected bundle; do not independently spin or detach the head. Hair, bow and sleeves wrap softly around the rolling silhouette and follow one beat late without covering the face in every frame. Keep the character center fixed and the invisible ground contact stable. Both tiny feet are visible in upright and crouched frames and remain readable as small tucked feet during the rotation. No long neck, no exposed belly, no underwear and no missing costume pieces.

EXACT FLOW: frame 1 upright hungry pout with both feet planted; frame 2 deep 6-percent mochi squash; frame 3 crouch and tip RIGHT by 15 degrees; frame 4 tip 35 degrees with hair spreading at contact; frame 5 side pose around 60 degrees; frame 6 curled diagonal around 90 degrees; frame 7 compact roll around 120 degrees; frame 8 upside-down-ish around 155 degrees with the skirt and hair still modestly enclosing the body; frame 9 passes 190 degrees; frame 10 reaches 225 degrees; frame 11 reaches 270 degrees; frame 12 reaches 315 degrees; frame 13 returns to a low upright crouch with a wide jelly squash; frame 14 elastic rebound upward; frame 15 cheeks and bow overshoot; frame 16 exact stable upright hungry settle. Adjacent rotation changes are small, continuous and consistent in one direction. Exaggeration comes from the round mochi silhouette, cheek compression and delayed hair/bow wobble.

STRICTLY FORBIDDEN: lying still for multiple middle frames, teleporting, changing roll direction, detached head/hair/bow, bare skin, exposed underwear, pain, injury, tears, stars, spiral eyes, new pupils, hollow eyes, tongue, drool, food, icon, floor, window, shadow, text, punctuation, motion lines, particles, props, extra characters or cropped body parts.`;

prompts.lick += `
CONTINUITY AND CUTENESS OVERRIDE (highest priority): keep her upright on both feet for all 16 frames. Never sit, lie down, kneel, collapse, pancake against the floor, or hide either foot. The top of her head, face center, waist and foot baseline must travel along one smooth arc; adjacent frames may change body height by at most 4 percent, face position by at most 5 percent of head width, and lean angle by at most 5 degrees. Her minimum height is 82 percent of her neutral standing height, including the anticipation and jelly-squash frames. All squash is a plump standing mochi compression, not a prone pose.

The tongue must read as a tiny delicious taste-test, not a giant inhale or a long animal tongue. At maximum extension it is only 20 to 24 percent of the cell width from lips to tip, broad and softly rounded, with nearly uniform width and a blunt marshmallow-like end. It extends straight LEFT from the small visible mouth, never covers the nose, cheek or eye, and never becomes longer than the width of her face. Her greedy acting comes from cheek puff, head tilt, hands tucked excitedly at the bow, and delayed hair/sleeve bounce—not from making the tongue enormous.

REVISED EXACT IN-BETWEEN MAP: frame 1 neutral notice; frame 2 head tilts LEFT by 3 degrees and hands rise slightly; frame 3 upright body compresses only 4 percent with both feet planted; frame 4 lean LEFT 5 degrees; frame 5 lean LEFT 9 degrees and tongue peeks out; frame 6 lean LEFT 13 degrees and tongue extends one-third; frame 7 lean LEFT 16 degrees and tongue extends two-thirds; frame 8 lean LEFT 18 degrees and the short broad tip reaches the fixed invisible icon contact point; frame 9 near-duplicate of frame 8 with only cheeks and sleeves wobbling; frame 10 near-duplicate with the tongue starting to release but its tip moving less than 3 percent; frame 11 lean LEFT 14 degrees and tongue retracts to two-thirds; frame 12 lean LEFT 10 degrees and tongue retracts to one-third; frame 13 lean LEFT 5 degrees, tongue fully gone, cheeks puffed; frame 14 upright standing mochi compression of only 5 percent with both feet still separate and visible; frame 15 upright 4-percent elastic rebound; frame 16 neutral pleased settle. Absolutely no detached white marks, emphasis marks, motion marks, sparkle strokes, sound symbols or decorative fragments in any cell.`;

// Continuity-focused replacement for the chomp prompt. This deliberately uses
// a strict frame map so the image model produces in-betweens instead of a pose
// board. Generation still receives only the verified original character image.
prompts.chomp = `${sharedPrompt}
ANIMATION: one continuous, exceptionally cute and exaggerated 16-frame LEFT-FACING inhale-and-eat action. CONTINUITY IS THE HIGHEST PRIORITY. This must look like one animator drew consecutive in-between frames, never like 16 unrelated key poses. The invisible app icon is on the LEFT; the character remains on its RIGHT and faces LEFT.

PERMANENT COMPOSITION LOCK: use exactly the same camera, character scale, head size, costume proportions and bottom contact point in every cell. From frames 3 through 11, keep her pelvis/feet anchored to the same point and move her head along one smooth shallow arc. Adjacent frames may change body rotation by at most 6 degrees, head position by at most 6 percent of head width, and silhouette size by at most 5 percent. Never independently recenter a pose. Never jump from standing to lying down, never teleport the head, never reverse facing direction, and never change from side-facing to front-facing during suction.

MOUTH DESIGN: keep the normal rounded outer face silhouette with no muzzle or projection. During maximum suction, open a huge vertical rounded bean-shaped mouth INSIDE the left side of the round face, 52 to 60 percent of head height and 36 to 44 percent of head width. Show a thick soft peach-pink rim, warm coral/cherry-red inner mouth and a small lighter pink tongue at the bottom. The cavity is colorful, softly shaded and inviting, never black. Keep both pupil-less pink-to-pale-blue gradient eyes visible above and behind the mouth. The mouth center must remain in nearly the same place relative to the head from frames 6 through 10 so a real overlaid icon can enter it.

EXACT FRAME-BY-FRAME IN-BETWEEN MAP:
1 neutral greedy notice, mouth closed, hands low;
2 hands lift slightly, shoulders rise, body compresses 4 percent;
3 lean 6 degrees RIGHT, tiny anticipatory pout;
4 lean 12 degrees RIGHT, mouth opens to 18 percent, hair lags slightly;
5 lean 18 degrees RIGHT, mouth opens to 32 percent;
6 lean 24 degrees RIGHT, mouth opens to 45 percent;
7 lean 30 degrees RIGHT, mouth opens to 55 percent;
8 lean 34 degrees RIGHT, mouth opens to 60 percent, strongest suction;
9 hold almost the same pose as frame 8, mouth remains 60 percent, only hair and sleeves trail farther RIGHT;
10 recoil gently to 30 degrees, mouth 52 percent, same feet and same mouth center;
11 recoil to 24 degrees, mouth 38 percent as the invisible icon finishes entering;
12 return toward 14 degrees, mouth closes and cheeks begin inflating;
13 nearly upright, both cheeks visibly puffed;
14 upright low mochi squash with larger puffed cheeks;
15 smooth upward swallow stretch, no more than 12 percent taller than frame 14;
16 settle into a smug sleepy satisfied standing pose with one hand near the fully clothed belly.

The whole motion may be dramatic across all 16 frames, but every adjacent pair must be a small readable step. Exaggeration comes from cumulative lean, huge internal mouth opening, soft body compression, and delayed hair/bow/sleeve follow-through—not from teleporting or changing pose family. Keep both tiny feet visible in every frame. Draw absolutely no food, file, icon, prop, suction particles, motion marks or punctuation because the program overlays the real icon. STRICTLY FORBIDDEN: prone/lying pose, sudden sitting pose, sudden front view, tiny mouth in frames 7-10, protruding lips, stretched face, muzzle, snout, tube, trumpet, beak, external tunnel, black void, teeth, saliva, horror expression, missing eyes, new pupils, holding or gripping anything. Keep complete head, open mouth, hair, costume, hands and both feet inside every cell with generous magenta clearance.`;

prompts.chomp += `
CRITICAL MAXIMUM-SUCTION EXIT: frames 8 and 9 must be near-duplicates with the same 60-percent open mouth, same head size, same face angle and same body anchor. Frame 10 must still have a very large 52-percent mouth; frame 11 must still have a clearly large 42-percent mouth. Reduce the opening by only about 8 to 10 percentage points per frame. Do not snap from the maximum mouth to a small O-mouth. Do not return upright until frame 12. The closing arc must be just as smooth and incremental as the opening arc.`;

// Every interaction sheet below is generated from the verified original image.
// Existing runtime frames are never sent back to the image model.
prompts.drag = `${sharedPrompt}
ANIMATION: a seamless 16-frame CUTE LIFTED-AND-DRAGGED loop. An invisible human hand gently holds the desktop pet from just above the top-center of her fluffy hair, but draw NO hand, cursor, grab icon, string or prop. The program physically moves the whole sprite, so keep one fixed camera and do not animate screen translation. Her enormous round head, tiny compact body, gray-blue hair, red-and-white outfit, large red bow, filled pupil-less gradient eyes and two tiny feet must remain completely visible.

ACTING AND PHYSICS: she is a very soft mochi being carried through the air. The upper hair compresses only slightly toward the invisible hold point while the connected head and clothed body hang beneath it; never detach or pinch off hair. Both feet dangle and kick gently. The body stretches vertically by at most 9 percent, then rebounds round; cheeks, sleeves, bow, skirt and long hair sway one beat late from left to right. Her expression is cute, mildly surprised and a little pouty, never hurt or frightened.

LOOP FLOW: frames 1-3 compact lifted pose with tiny dangling feet; frames 4-6 body and bow lag softly LEFT while the head remains stable; frames 7-9 pass through center with one Q-bouncy vertical stretch and cheek wobble; frames 10-12 hair, sleeves and feet lag softly RIGHT; frames 13-16 rebound through a plump mochi squash and return seamlessly to frame 1. Adjacent frames are small true in-betweens with stable head scale and no camera recentering. Keep at least 14 percent empty magenta above the hair even though the invisible grab point is above it.

STRICTLY FORBIDDEN: visible hand, fingers, cursor, rope, hook, clothespin, halo, motion lines, particles, text, detached hair, long neck, stretched bare skin, exposed belly, missing feet, cropped head, hollow eyes, new pupils, mouse gaze tracking, pain, tears or horror.`;

prompts.drag += `
CRITICAL CELL-SAFETY AND CONTINUITY OVERRIDE (highest priority): every one of the 16 characters must remain UPRIGHT and centered inside its own cell. The complete silhouette may occupy only 48 to 54 percent of cell height. Keep at least 20 percent completely empty magenta above the highest hair point and at least 18 percent completely empty magenta below the lowest foot in EVERY cell. No part of one frame may enter the row above, row below or neighboring column. The character center may move by no more than 2 percent of cell width or height between adjacent frames.

Never rotate the whole body beyond 7 degrees. Never sit, lie down, recline, become horizontal, drop toward the cell bottom, or alternate between standing and side-lying poses. All frames show the same upright dangling pose family: head at the same height, torso directly beneath the head, both little feet dangling directly beneath the skirt. Animate the carried feeling only through a gentle 4-percent vertical stretch/compression, tiny alternating foot kicks, cheek wobble, and delayed hair/bow/sleeve sway. Frames 1-4 sway 0 to 4 degrees LEFT; frames 5-8 pass upright through center; frames 9-12 sway 0 to 4 degrees RIGHT; frames 13-16 return through center to frame 1. Draw no detached white vibration marks or any other detached marks.`;

prompts.jump = `${sharedPrompt}
ANIMATION: one non-looping 16-frame CUTE TERRAIN JUMP for a desktop pet leaping from one invisible Windows window edge toward another. She faces RIGHT; the program may mirror the finished frames and physically moves her along the ballistic arc. Draw NO window, ledge, ground, shadow or scenery. Keep one fixed camera and never translate the character across the cells.

PHYSICS AND FLOW: frame 1 stable standing with both feet visible; frame 2 knees/body compress 5 percent; frame 3 deepest plump anticipation squash, hands and sleeves draw in; frame 4 elastic takeoff with both feet just leaving the invisible ledge; frames 5-7 compact upward flight, tiny feet tucked but still clearly drawn, hair and bow lag DOWN; frames 8-9 apex pose as one round buoyant mochi ball, cheeks puffed and limbs slightly open; frames 10-12 begin descending, feet extend to search for a landing while hair floats UP one beat late; frames 13-14 body stretches downward by at most 8 percent; frames 15-16 brace gently for the program's separate landing animation. Her expression changes from determined greedy delight to cute airborne surprise, with a small warm mouth and stable filled pupil-less gradient eyes.

The huge head and tiny torso remain one compact connected unit; chin-to-bow spacing and head scale stay constant within 3 percent. Adjacent frames are true in-betweens with no teleporting or pose-family jump. Both tiny feet remain present in every frame even when tucked.

STRICTLY FORBIDDEN: drawn platform, window, floor, shadow, speed lines, dust, arrows, particles, text, long neck, noodle torso, detached head, prone pose, new pupils, hollow eyes, tears, injury, cropped head/hair or missing feet.`;

prompts.slide = `${sharedPrompt}
ANIMATION: a seamless 16-frame CUTE SLIDE-DOWN-A-WINDOW-EDGE loop. She is outside a real Windows window and faces RIGHT, gently hugging an invisible vertical edge located near 70 percent of each cell width. The program moves her downward and mirrors the sheet for the opposite side. Draw NO window, wall, ledge, rope, cursor, handhold or scenery. Keep one fixed camera; do not animate overall downward translation inside the cells.

CONTACT AND POSE: both soft sleeves/hands press around the SAME invisible vertical contact line in every frame while the compact clothed body hangs to its LEFT. Her enormous round head, short torso, long gray-blue hair, red-white outfit, large bow, filled pupil-less gradient eyes and both tiny feet remain complete. She is cautiously delighted, not scared: puffed cheeks, tiny effort smile, warm soft acting. Hair, bow, skirt and feet stream slightly UP from the downward motion and wobble one beat late.

LOOP FLOW: frames 1-3 hands squeeze softly and the body stretches downward only 5 percent; frames 4-6 one foot reaches lower while the other tucks, both still visible; frames 7-9 body compresses into a plump friction squash and cheeks puff; frames 10-12 the feet switch with a small elastic downward slip; frames 13-16 sleeves, hair and bow overshoot, then return seamlessly to frame 1. The invisible edge contact stays fixed within 2 percent across all frames. Adjacent poses are incremental, with stable head size and no recentering.

STRICTLY FORBIDDEN: drawn window/wall/rope/ladder, ground, shadow, cursor, hand, long arms, stretched neck, detached hair, missing feet, exposed skin, new pupils, hollow eyes, tears, injury, motion lines, text, punctuation, particles, props or cropped body parts.`;

const requested = process.argv.slice(2);
const actions = requested.length ? requested : Object.keys(prompts);
for (const action of actions) {
  if (!(action in prompts)) {
    throw new Error(`Unknown action: ${action}`);
  }
}

if (!accountName || !sourceImage || !databasePath) {
  throw new Error("Set IMAGE_EDIT_USERNAME, IMAGE_EDIT_SOURCE_IMAGE and IMAGE_EDIT_DATABASE_PATH explicitly before using the local image service.");
}
const serviceUrl = new URL(apiBase);
if (!["localhost", "127.0.0.1", "[::1]"].includes(serviceUrl.hostname) ||
    !["http:", "https:"].includes(serviceUrl.protocol) || serviceUrl.username || serviceUrl.password ||
    serviceUrl.search || serviceUrl.hash || serviceUrl.pathname !== "/") {
  throw new Error("IMAGE_EDIT_BASE must be a loopback service origin without credentials or URL parameters.");
}
const originalSourceBytes = await fs.readFile(sourceImage);
const sourceSha256 = crypto.createHash("sha256").update(originalSourceBytes).digest("hex");
if (sourceSha256 !== expectedSourceSha256) {
  throw new Error(
    `Original source verification failed. Expected ${expectedSourceSha256}, received ${sourceSha256}. ` +
    "Generated sprites must never be used as image inputs."
  );
}

const db = new DatabaseSync(databasePath);
const user = db.prepare("SELECT id, username, role FROM users WHERE username = ?").get(accountName);
if (!user || user.role !== "admin") {
  throw new Error("The configured admin account was not found.");
}

const sessionToken = crypto.randomBytes(32).toString("base64url");
const sessionHash = crypto.createHash("sha256").update(sessionToken).digest("hex");
db.prepare("INSERT INTO sessions (token_hash, user_id, expires_at, created_at) VALUES (?, ?, ?, ?)").run(
  sessionHash,
  user.id,
  Date.now() + 30 * 60 * 1000,
  new Date().toISOString(),
);

async function generate(action) {
  const form = new FormData();
  form.append("images", new Blob([originalSourceBytes], { type: "image/png" }), "original-character-reference.png");
  form.append("prompt", prompts[action]);
  form.append("imageModel", model);
  form.append("apiProvider", "auto");
  form.append("aspectRatio", "1:1");
  form.append("resolution", "2k");
  form.append("quality", "high");
  form.append("background", "opaque");
  form.append("outputFormat", "png");
  form.append("moderation", "low");
  form.append("n", "1");
  form.append("preset", "none");
  form.append("stylePreset", "none");

  const response = await fetch(`${apiBase}/api/edit`, {
    method: "POST",
    headers: {
      Cookie: `image_edit_web_session=${sessionToken}`,
      "X-API-Provider": "auto",
    },
    body: form,
  });
  const result = await response.json();
  if (!response.ok) {
    throw new Error(`${action} failed (${response.status}): ${result.error || "unknown API error"}`);
  }
  if (result.model !== model || !result.images?.[0]?.url) {
    throw new Error(`${action} returned an unexpected model or no image.`);
  }

  const imageResponse = await fetch(`${apiBase}${result.images[0].url}`, {
    headers: { Cookie: `image_edit_web_session=${sessionToken}` },
  });
  if (!imageResponse.ok) {
    throw new Error(`${action} output download failed (${imageResponse.status}).`);
  }

  const imagePath = path.join(outputRoot, `${action}_sheet_api.png`);
  const promptPath = path.join(outputRoot, `${action}_prompt.txt`);
  await fs.writeFile(imagePath, Buffer.from(await imageResponse.arrayBuffer()));
  await fs.writeFile(promptPath, prompts[action].trim() + "\n", "utf8");
  return {
    action,
    imagePath,
    promptPath,
    serverFilename: result.images[0].filename,
    model: result.model,
    size: result.size,
    timings: result.timings,
  };
}

try {
  await fs.mkdir(outputRoot, { recursive: true });
  for (const action of actions) {
    process.stdout.write(`Generating ${action} with ${model}...\n`);
  }
  const results = await Promise.all(actions.map(async (action) => {
    const result = await generate(action);
    process.stdout.write(`Saved ${action}.\n`);
    return result;
  }));
  let previousResults = [];
  try {
    const previousManifest = JSON.parse(await fs.readFile(path.join(outputRoot, "generation-manifest.json"), "utf8"));
    previousResults = Array.isArray(previousManifest.results) ? previousManifest.results : [];
  } catch {
    previousResults = [];
  }
  const mergedResults = new Map(previousResults.map((item) => [item.action, item]));
  for (const item of results) {
    mergedResults.set(item.action, item);
  }
  const manifest = {
    generatedAt: new Date().toISOString(),
    service: "image_edit_web",
    endpoint: "/api/edit",
    model,
    sourceImage,
    sourceSha256,
    lineagePolicy: "original-image-only; generated sprites are forbidden as model inputs",
    results: [...mergedResults.values()].sort((a, b) => a.action.localeCompare(b.action)),
  };
  await fs.writeFile(path.join(outputRoot, "generation-manifest.json"), JSON.stringify(manifest, null, 2), "utf8");
  process.stdout.write(JSON.stringify({ ok: true, actions, outputRoot }, null, 2) + "\n");
} finally {
  db.prepare("DELETE FROM sessions WHERE token_hash = ?").run(sessionHash);
  db.close();
}
