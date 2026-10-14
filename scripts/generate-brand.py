from pathlib import Path
from PIL import Image, ImageDraw
assets = Path(__file__).resolve().parents[1] / 'src/DesktopTidy/Assets'
assets.mkdir(exist_ok=True)
im = Image.new('RGBA', (1024, 1024))
d = ImageDraw.Draw(im)
d.rounded_rectangle((0, 0, 1023, 1023), radius=240, fill='#27656a')
d.rounded_rectangle((224, 224, 456, 800), radius=64, fill='#edf5f5')
d.rounded_rectangle((520, 224, 800, 464), radius=64, fill='#edf5f5')
d.rounded_rectangle((520, 528, 800, 800), radius=64, fill='#99cecd')
im.resize((256, 256), Image.Resampling.LANCZOS).save(assets / 'desktop-tidy.png')
im.save(assets / 'desktop-tidy.ico', sizes=[(16,16),(20,20),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
