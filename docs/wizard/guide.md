# Balls Wizard Guide

This guide matches the Balls version that packaged it. Use only these documented workflows for
actionable help. If a requested workflow is absent, say that this version does not document it.

## What Balls is

Balls is an open-source graphical integration platform for trusted Circles. A Circle is the
top-level shared digital environment. People join as Members; computers participate as Nodes;
Nodes explicitly contribute bounded Capabilities. Membership does not automatically expose a
computer or authorize every Capability.

Circle AI is a contributed shared Capability. Balls Wizard is different: Wizard is optional local
product help on the computer asking the question. Wizard has no tools and no Circle authority.

## Create a Circle

Open Balls from its normal shortcut. If this Node has no Circles, choose **Create a Circle**, enter
the Circle name and your display name, then choose **Create Circle**. Balls creates the local Owner
and Node state. Creating a Circle does not contribute folders, compute, or other resources.

## Join a Circle

Open Balls from its normal shortcut, choose **Join a Circle**, paste the private invitation from
the Circle Owner, enter your display name, and choose **Join Circle**. The invitation is private,
single-use, time-bounded Circle data. Joining establishes Membership; it does not contribute files
from the joining computer.

## Invite a Member

An Owner opens the Circle workspace and chooses **Create invitation**. Send the resulting private
invitation only to the intended trusted person. The recipient opens Balls and follows **Join a
Circle**. If invitation creation reports that the private network is unavailable, reopen Balls
normally on the Owner Node and confirm the participating computers are on the same private LAN.

## Contribute a folder

An Owner opens the Circle workspace, chooses **Choose existing folder**, selects the intended local
folder in the Windows picker, reviews the exact folder preview, and approves **Contribute**. Existing
files stay in place. Balls does not treat ordinary folder contents as Balls-owned data and does not
delete them when access is later removed.

Folder contribution is explicit. Joining a Circle never contributes a personal folder.

## Give a Member access to Circle Files

After a folder is contributed and the intended person has joined, the Owner chooses the folder and
Member in the graphical access panel, reviews the plain-language Read/write preview, and approves
sharing. Balls manages the narrow Windows provider credential underneath the Circle Access Grant;
the Member should not handle an SMB password, endpoint, account name, plan ID, or drive-mapping
details.

## Open Circle Files as a Member

The Member opens the Circle workspace and chooses **Open shared folder in Explorer**. Balls syncs
the current Access Grant, chooses a supported free drive automatically, maps the exact authorized
folder, and opens its root in Windows File Explorer. If the folder is offline, confirm that the
Owner computer hosting it is running and reachable on the private network, then try the same button
again.

## Membership and access are separate

A Member may belong to a Circle without access to every Capability. An Owner must explicitly grant
Circle Files access to a contributed folder. Removing or changing a Capability Grant is distinct
from changing Membership.

This packaged version does not document a graphical Member-removal workflow. Do not invent a CLI
command or manual provider cleanup procedure. Explain that the workflow is unavailable in this
version and direct the user to the current Balls release notes or issue tracker.

## Balls Wizard privacy and removal

Wizard runs on the requesting Windows Node. Chat text and its bounded Wizard System Context are
sent only to the local llama.cpp process. Conversations are kept only in the current browser
session. Choose **Clear conversation** to remove the visible session history.

Open the Wizard details to inspect **What Wizard can see**. Choose **Remove Wizard** to stop the
local runtime and delete Wizard-owned model, runtime, partial download, cache, and install state.
Removing Wizard does not remove Balls, leave a Circle, revoke a Capability, or delete user files.

## Unsupported requests

Wizard v0 cannot run a command, edit a file, click a product control, change Windows, mutate Circle
state, inspect arbitrary logs, or use Circle content. When this guide does not support actionable
instructions, say so plainly. Harmless greetings and floating-ball wizard banter do not require a
documentation source.
