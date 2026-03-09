import os
from PIL import Image
import torch.utils.data as data
import torchvision.transforms as transforms
import random
import numpy as np
from PIL import ImageEnhance

# several data augumentation strategies
def cv_random_flip(img, fix, gt):
    """Randomly flip image, fixation map, and ground truth horizontally with 50% probability."""
    flip_flag = random.randint(0, 1)
    # flip_flag2= random.randint(0,1)
    # left right flip
    if flip_flag == 1:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
        fix = fix.transpose(Image.FLIP_LEFT_RIGHT)
        gt = gt.transpose(Image.FLIP_LEFT_RIGHT)
    return img, fix, gt


def randomCrop(image, fix, gt):
    """Randomly crop image, fixation map, and ground truth within a 30-pixel border margin."""
    border = 30
    image_width = image.size[0]
    image_height = image.size[1]
    crop_win_width = np.random.randint(image_width - border, image_width)
    crop_win_height = np.random.randint(image_height - border, image_height)
    random_region = (
        (image_width - crop_win_width) >> 1, (image_height - crop_win_height) >> 1, (image_width + crop_win_width) >> 1,
        (image_height + crop_win_height) >> 1)
    return image.crop(random_region), fix.crop(random_region), gt.crop(random_region)


def randomRotation(image, fix, gt):
    """Randomly rotate image, fixation map, and ground truth by up to ±15 degrees (80% chance)."""
    mode = Image.BICUBIC
    if random.random() > 0.8:
        random_angle = np.random.randint(-15, 15)
        image = image.rotate(random_angle, mode)
        fix = fix.rotate(random_angle, mode)
        gt = gt.rotate(random_angle, mode)
    return image, fix, gt


def colorEnhance(image):
    """Apply random brightness, contrast, color saturation, and sharpness jitter to an image."""
    bright_intensity = random.randint(5, 15) / 10.0
    image = ImageEnhance.Brightness(image).enhance(bright_intensity)
    contrast_intensity = random.randint(5, 15) / 10.0
    image = ImageEnhance.Contrast(image).enhance(contrast_intensity)
    color_intensity = random.randint(0, 20) / 10.0
    image = ImageEnhance.Color(image).enhance(color_intensity)
    sharp_intensity = random.randint(0, 30) / 10.0
    image = ImageEnhance.Sharpness(image).enhance(sharp_intensity)
    return image


def randomGaussian(image, mean=0, sigma=0.15):
    """Add Gaussian noise to a grayscale image (low-noise variant: sigma=0.15)."""
    def gaussianNoisy(im, mean=mean, sigma=sigma):
        """Add independent Gaussian noise to each pixel value in a flattened array."""
        for _i in range(len(im)):
            im[_i] += random.gauss(mean, sigma)
        return im

    img = np.asarray(image)
    width, height = img.shape
    img = gaussianNoisy(img[:].flatten(), mean, sigma)
    img = img.reshape([width, height])
    return Image.fromarray(np.uint8(img))

def randomGaussian1(image, mean=0.1, sigma=0.35):
    """Add Gaussian noise to a grayscale image (high-noise variant: mean=0.1, sigma=0.35)."""
    def gaussianNoisy(im, mean=mean, sigma=sigma):
        """Add independent Gaussian noise to each pixel value in a flattened array."""
        for _i in range(len(im)):
            im[_i] += random.gauss(mean, sigma)
        return im

    img = np.asarray(image)
    width, height = img.shape
    img = gaussianNoisy(img[:].flatten(), mean, sigma)
    img = img.reshape([width, height])
    return Image.fromarray(np.uint8(img))


def randomPeper(img):
    """Add salt-and-pepper noise to an image (0.15% of pixels randomly set to 0 or 255)."""
    img = np.array(img)
    noiseNum = int(0.0015 * img.shape[0] * img.shape[1])
    for i in range(noiseNum):

        randX = random.randint(0, img.shape[0] - 1)

        randY = random.randint(0, img.shape[1] - 1)

        if random.randint(0, 1) == 0:

            img[randX, randY] = 0

        else:

            img[randX, randY] = 255
    return Image.fromarray(img)

class SalObjDataset(data.Dataset):
    """PyTorch Dataset for salient object detection training.

    Loads triplets of (RGB image, ground-truth mask, fixation map) and applies
    a full suite of data augmentation: random flip, crop, rotation, color jitter,
    and salt-and-pepper noise.
    """
    def __init__(self, image_root, gt_root, fix_root, trainsize):
        """Initialize dataset paths and transforms.

        Parameters
        ----------
        image_root : str
            Directory containing RGB input images (.jpg).
        gt_root : str
            Directory containing ground-truth binary masks (.jpg or .png).
        fix_root : str
            Directory containing fixation maps (.png).
        trainsize : int
            Target square size for resizing inputs.
        """
        self.trainsize = trainsize
        self.images = [image_root + f for f in os.listdir(image_root) if f.endswith('.jpg')]
        self.gts = [gt_root + f for f in os.listdir(gt_root) if f.endswith('.jpg')
                    or f.endswith('.png')]
        self.fixs = [fix_root + f for f in os.listdir(fix_root) if f.endswith('.png')]
        self.images = sorted(self.images)
        self.gts = sorted(self.gts)
        self.fixs = sorted(self.fixs)
        self.filter_files()
        self.size = len(self.images)
        self.img_transform = transforms.Compose([
            transforms.Resize((self.trainsize, self.trainsize)),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225])])
        self.gt_transform = transforms.Compose([
            transforms.Resize((self.trainsize, self.trainsize)),
            transforms.ToTensor()])
        self.fix_transform = transforms.Compose([
            transforms.Resize((self.trainsize, self.trainsize)),
            transforms.ToTensor()])

    def __getitem__(self, index):
        """Load and augment a single (image, gt, fixation) triplet by index."""
        image = self.rgb_loader(self.images[index])
        gt = self.binary_loader(self.gts[index])
        fix = self.binary_loader(self.fixs[index])
        image, fix, gt = cv_random_flip(image, fix, gt)
        image, fix, gt = randomCrop(image, fix, gt)
        image, fix, gt = randomRotation(image, fix, gt)
        image = colorEnhance(image)
        # gt=randomGaussian(gt)
        gt = randomPeper(gt)
        image = self.img_transform(image)
        gt = self.gt_transform(gt)
        fix = self.gt_transform(fix)
        return image, gt, fix

    def filter_files(self):
        """Remove triplets where image and mask sizes do not match."""
        assert len(self.images) == len(self.gts)
        assert len(self.images) == len(self.fixs)
        images = []
        gts = []
        fixs = []
        for img_path, gt_path, fix_path in zip(self.images, self.gts, self.fixs):
            img = Image.open(img_path)
            gt = Image.open(gt_path)
            fix = Image.open(fix_path)
            if img.size == gt.size:
                images.append(img_path)
                gts.append(gt_path)
                fixs.append(fix_path)
        self.images = images
        self.gts = gts
        self.fixs = fixs

    def rgb_loader(self, path):
        """Load an image from disk and convert to RGB mode."""
        with open(path, 'rb') as f:
            img = Image.open(f)
            return img.convert('RGB')

    def binary_loader(self, path):
        """Load an image from disk and convert to grayscale (L mode)."""
        with open(path, 'rb') as f:
            img = Image.open(f)
            # return img.convert('1')
            return img.convert('L')

    def resize(self, img, gt, fix):
        """Upscale images to at least trainsize if either dimension is too small."""
        assert img.size == gt.size
        assert img.size == fix.size
        w, h = img.size
        if h < self.trainsize or w < self.trainsize:
            h = max(h, self.trainsize)
            w = max(w, self.trainsize)
            return img.resize((w, h), Image.BILINEAR), gt.resize((w, h), Image.NEAREST), fix.resize((w, h), Image.NEAREST)
        else:
            return img, gt, fix

    def __len__(self):
        """Return the total number of samples in the dataset."""
        return self.size


def get_loader(image_root, gt_root, fix_root, batchsize, trainsize, shuffle=True, num_workers=12, pin_memory=True):
    """Build a DataLoader for training from parallel image/gt/fixation directories.

    Parameters
    ----------
    image_root, gt_root, fix_root : str
        Directories containing RGB images, ground-truth masks, and fixation maps.
    batchsize : int
        Number of samples per batch.
    trainsize : int
        Target square size for all inputs.
    shuffle, num_workers, pin_memory : passed directly to DataLoader.
    """
    dataset = SalObjDataset(image_root, gt_root, fix_root, trainsize)
    data_loader = data.DataLoader(dataset=dataset,
                                  batch_size=batchsize,
                                  shuffle=shuffle,
                                  num_workers=num_workers,
                                  pin_memory=pin_memory)
    return data_loader


class test_dataset:
    """Lightweight sequential dataset for inference on a folder of JPG images."""

    def __init__(self, image_root, testsize):
        """Initialize with the image directory and the target resize dimension."""
        self.testsize = testsize
        self.images = [image_root + f for f in os.listdir(image_root) if f.endswith('.jpg')]
        self.images = sorted(self.images)
        self.transform = transforms.Compose([
            transforms.Resize((self.testsize, self.testsize)),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225])])
        self.size = len(self.images)
        self.index = 0

    def load_data(self):
        """Load the next image in sequence; returns (tensor, orig_W, orig_H, name)."""
        image = self.rgb_loader(self.images[self.index])
        HH = image.size[0]
        WW = image.size[1]
        image = self.transform(image).unsqueeze(0)
        name = self.images[self.index].split('/')[-1]
        if name.endswith('.jpg'):
            name = name.split('.jpg')[0] + '.png'
        self.index += 1
        return image, HH, WW, name

    def rgb_loader(self, path):
        """Load an image from disk and convert to RGB mode."""
        with open(path, 'rb') as f:
            img = Image.open(f)
            return img.convert('RGB')

    def binary_loader(self, path):
        """Load an image from disk and convert to grayscale (L mode)."""
        with open(path, 'rb') as f:
            img = Image.open(f)
            return img.convert('L')